using Agnosia.Models;

namespace Agnosia.Android.Vpn;

internal readonly record struct VpnRestoreCompletionResult(
    OperationResult Result,
    bool OwnerMatched);

internal readonly record struct VpnRestorePackageStateResult(
    OperationResult Result,
    bool Installed,
    bool Hidden,
    string? LaunchId)
{
    public static VpnRestorePackageStateResult Success(
        bool installed,
        bool hidden,
        string? launchId)
    {
        return new VpnRestorePackageStateResult(
            OperationResult.Success("Work package state was read."),
            installed,
            hidden,
            launchId);
    }

    public static VpnRestorePackageStateResult Failure(string message)
    {
        return new VpnRestorePackageStateResult(
            OperationResult.Failure(message),
            false,
            false,
            null);
    }
}

internal readonly record struct VpnRestoreRecoveryResult(
    OperationResult Result,
    bool RestoreSucceeded);

internal sealed class VpnRestoreOwnershipCoordinator
{
    private readonly Func<string?> _readState;
    private readonly Action<string> _writeState;
    private readonly Action _removeState;
    private readonly Func<bool> _readLegacyFlag;
    private readonly Action _clearLegacyFlag;
    private readonly Func<string> _createLaunchId;
    private readonly Func<VpnRestoreOwner, CancellationToken, Task<VpnRestorePackageStateResult>>?
        _queryPackageState;
    private readonly Func<VpnRestoreOwner, CancellationToken, Task<OperationResult>>? _confirmRecovery;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public VpnRestoreOwnershipCoordinator(
        Func<string?> readState,
        Action<string> writeState,
        Action removeState,
        Func<bool> readLegacyFlag,
        Action clearLegacyFlag,
        Func<string>? createLaunchId = null,
        Func<VpnRestoreOwner, CancellationToken, Task<VpnRestorePackageStateResult>>? queryPackageState = null,
        Func<VpnRestoreOwner, CancellationToken, Task<OperationResult>>? confirmRecovery = null)
    {
        _readState = readState ?? throw new ArgumentNullException(nameof(readState));
        _writeState = writeState ?? throw new ArgumentNullException(nameof(writeState));
        _removeState = removeState ?? throw new ArgumentNullException(nameof(removeState));
        _readLegacyFlag = readLegacyFlag ?? throw new ArgumentNullException(nameof(readLegacyFlag));
        _clearLegacyFlag = clearLegacyFlag ?? throw new ArgumentNullException(nameof(clearLegacyFlag));
        _createLaunchId = createLaunchId ?? (() => Guid.NewGuid().ToString("N"));
        _queryPackageState = queryPackageState;
        _confirmRecovery = confirmRecovery;
    }

    public async Task<OperationResult> ExecuteLaunchAsync(
        string packageName,
        Func<VpnRestoreLaunchScope, CancellationToken, Task<OperationResult>> execute,
        Func<Task<OperationResult>> restore,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(restore);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!TryLoadState(out var state, out var loadFailure)) return loadFailure;

            if (state.RestoreReady)
            {
                var readyRecovery = await RestoreAndClearAsync(state, null, restore, cancellationToken)
                    .ConfigureAwait(false);
                if (!readyRecovery.Result.Succeeded) return readyRecovery.Result;
                state = VpnRestoreOwnershipState.Empty;
            }

            var pendingRecovery = await ReconcilePendingOwnerAsync(state, restore, cancellationToken)
                .ConfigureAwait(false);
            state = pendingRecovery.State;
            if (!pendingRecovery.Result.Succeeded) return pendingRecovery.Result;

            var owner = new VpnRestoreOwner(_createLaunchId(), packageName);
            state = state.Begin(owner);
            PersistState(state);

            var scope = new VpnRestoreLaunchScope(
                owner,
                state.RestoreRequired,
                () => state,
                updated =>
                {
                    state = updated;
                    PersistState(state);
                },
                restore);

            try
            {
                var result = await execute(scope, cancellationToken).ConfigureAwait(false);
                if (result.Succeeded)
                {
                    if (Equals(state.PendingOwner, owner))
                    {
                        state = state.Commit(owner);
                        PersistState(state);
                    }

                    return result;
                }

                if (scope.AcquiredRestoreObligation && !scope.RollbackAttempted)
                    await scope.RollbackAsync().ConfigureAwait(false);
                AbortPendingOwner(owner, ref state);
                return result;
            }
            catch (Commands.WorkLaunchUnconfirmedException exception)
                when (exception.LaunchId == owner.LaunchId && exception.PackageName == owner.PackageName)
            {
                state = state with { PendingLaunchDispatched = true };
                PersistState(state);
                // Cancellation of the Activity cannot cancel our durable obligation.
                try
                {
                    var reconciliation = await ReconcilePendingOwnerAsync(state, restore, CancellationToken.None)
                        .ConfigureAwait(false);
                    if (reconciliation.Result.Succeeded && Equals(reconciliation.State.ActiveOwner, owner))
                        return OperationResult.Success("Рабочая сессия запуска подтверждена сверкой.");
                }
                catch (Exception) { /* Persisted pending owner is retried by the recovery scheduler. */ }
                return OperationResult.Failure(exception.Message);
            }
            catch
            {
                if (scope.AcquiredRestoreObligation && !scope.RollbackAttempted)
                    await scope.RollbackAsync().ConfigureAwait(false);
                AbortPendingOwner(owner, ref state);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<VpnRestoreRecoveryResult> RecoverAsync(
        Func<Task<OperationResult>> restore,
        CancellationToken cancellationToken = default,
        VpnRestoreOwner? expectedOwner = null)
    {
        ArgumentNullException.ThrowIfNull(restore);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!TryLoadState(out var state, out var loadFailure))
                return new VpnRestoreRecoveryResult(loadFailure, false);

            if (expectedOwner is not null && !Equals(state.PendingOwner ?? state.ActiveOwner, expectedOwner))
                return new(OperationResult.Success("Obsolete recovery ignored."), false);

            if (state.RestoreReady)
                return await RestoreAndClearAsync(state, null, restore, cancellationToken)
                    .ConfigureAwait(false);

            var pendingRecovery = await ReconcilePendingOwnerAsync(state, restore, cancellationToken)
                .ConfigureAwait(false);
            state = pendingRecovery.State;
            if (!pendingRecovery.Result.Succeeded || pendingRecovery.RestoreSucceeded)
            {
                return new VpnRestoreRecoveryResult(
                    pendingRecovery.Result,
                    pendingRecovery.RestoreSucceeded);
            }

            if (!state.RestoreRequired)
                return new VpnRestoreRecoveryResult(
                    OperationResult.Success("VPN restore is not pending."),
                    false);

            if (state.ActiveOwner is not { } activeOwner)
                return await RestoreAndClearAsync(state, null, restore, cancellationToken)
                    .ConfigureAwait(false);

            var packageState = await QueryPackageStateAsync(activeOwner, cancellationToken)
                .ConfigureAwait(false);
            if (!packageState.Result.Succeeded)
                return new VpnRestoreRecoveryResult(packageState.Result, false);

            if (packageState.Installed && !packageState.Hidden)
            {
                return MatchesLaunch(activeOwner, packageState.LaunchId)
                    ? new VpnRestoreRecoveryResult(
                        OperationResult.Success("The owned work launch is still visible."),
                        false)
                    : new VpnRestoreRecoveryResult(
                        OperationResult.Failure(
                            "The visible work launch does not match the pending VPN owner."),
                        false);
            }

            if (!string.IsNullOrWhiteSpace(packageState.LaunchId)
                && !MatchesLaunch(activeOwner, packageState.LaunchId))
            {
                return new VpnRestoreRecoveryResult(
                    OperationResult.Failure(
                        "The hidden work launch does not match the pending VPN owner."),
                    false);
            }

            var ownerToConfirm = MatchesLaunch(activeOwner, packageState.LaunchId)
                ? activeOwner
                : null;
            return await RestoreAndClearAsync(state, ownerToConfirm, restore, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<VpnRestoreRecoveryResult> RecoverAfterDeviceRestartAsync(
        VpnRestoreOwner? expectedOwner,
        Func<Task<OperationResult>> restore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(restore);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!TryLoadState(out var state, out var loadFailure))
                return new VpnRestoreRecoveryResult(loadFailure, false);
            if (!state.RestoreRequired)
                return new VpnRestoreRecoveryResult(
                    OperationResult.Success("VPN restore is not pending after device restart."),
                    false);

            if (!Equals(state.ActiveOwner ?? state.PendingOwner, expectedOwner))
                return new VpnRestoreRecoveryResult(OperationResult.Success("Stale restart recovery ignored."), false);

            state = state with
            {
                ActiveOwner = expectedOwner, PendingOwner = null, RestoreReady = true,
                ForceRestore = state.ForceRestore || state.PendingOwner is not null
            };
            PersistState(state);

            return await RestoreAndClearAsync(state, null, restore, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<VpnRestoreCompletionResult> AcceptCompletionAsync(
        string packageName, string? launchId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!TryLoadState(out var state, out var failure)) return new(failure, false);
            if (!state.MatchesCallback(packageName, launchId))
                return new(OperationResult.Success("Obsolete callback acknowledged."), false);
            PersistState(state.MarkRestoreReady(packageName, launchId));
            return new(OperationResult.Success("VPN restore accepted durably."), true);
        }
        finally { _gate.Release(); }
    }

    public async Task<VpnRestoreCompletionResult> CompleteOwnerAsync(
        string packageName,
        string? launchId,
        Func<Task<OperationResult>> restore,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentNullException.ThrowIfNull(restore);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!TryLoadState(out var state, out var loadFailure))
                return new VpnRestoreCompletionResult(loadFailure, false);
            if (!state.MatchesCallback(packageName, launchId))
            {
                return new VpnRestoreCompletionResult(
                    OperationResult.Success("VPN restore callback does not match the current owner."),
                    false);
            }

            state = state.MarkRestoreReady(packageName, launchId);
            PersistState(state);

            cancellationToken.ThrowIfCancellationRequested();
            var result = await restore().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Succeeded) PersistState(state.ClearAfterRestore());

            return new VpnRestoreCompletionResult(result, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _removeState();
            _clearLegacyFlag();
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryLoadState(
        out VpnRestoreOwnershipState state,
        out OperationResult failure)
    {
        var raw = _readState();
        if (!string.IsNullOrWhiteSpace(raw))
        {
            if (VpnRestoreOwnershipCodec.TryDeserialize(raw, out state))
            {
                failure = OperationResult.Success(string.Empty);
                return true;
            }

            _removeState();
            failure = OperationResult.Failure("Сохранённое обязательство восстановления VPN повреждено.");
            state = VpnRestoreOwnershipState.Empty;
            return false;
        }

        if (_readLegacyFlag())
        {
            state = VpnRestoreOwnershipState.Legacy;
            PersistState(state);
            _clearLegacyFlag();
            failure = OperationResult.Success(string.Empty);
            return true;
        }

        state = VpnRestoreOwnershipState.Empty;
        failure = OperationResult.Success(string.Empty);
        return true;
    }

    private void AbortPendingOwner(
        VpnRestoreOwner owner,
        ref VpnRestoreOwnershipState state)
    {
        if (!Equals(state.PendingOwner, owner)) return;

        state = state.Abort(owner);
        PersistState(state);
    }

    private async Task<PendingOwnerReconciliationResult> ReconcilePendingOwnerAsync(
        VpnRestoreOwnershipState state,
        Func<Task<OperationResult>> restore,
        CancellationToken cancellationToken)
    {
        if (state.PendingOwner is not { } pendingOwner)
            return PendingOwnerReconciliationResult.Unchanged(state);

        if (!state.RestoreRequired)
        {
            state = state.Abort(pendingOwner);
            PersistState(state);
            return PendingOwnerReconciliationResult.Unchanged(state);
        }

        var packageState = await QueryPackageStateAsync(pendingOwner, cancellationToken)
            .ConfigureAwait(false);
        if (!packageState.Result.Succeeded)
            return PendingOwnerReconciliationResult.Failure(state, packageState.Result);

        if (state.ActiveOwner is { } previousOwner
            && previousOwner.PackageName == pendingOwner.PackageName
            && MatchesLaunch(previousOwner, packageState.LaunchId))
        {
            if (state.PendingLaunchDispatched)
                return PendingOwnerReconciliationResult.Failure(state,
                    OperationResult.Failure("Рабочий профиль пока сообщает предыдущую сессию запуска."));
            state = state.Abort(pendingOwner);
            PersistState(state);
            return PendingOwnerReconciliationResult.Unchanged(state);
        }

        if (MatchesLaunch(pendingOwner, packageState.LaunchId))
        {
            state = state.Commit(pendingOwner);
            PersistState(state);
            if (!packageState.Hidden || !state.RestoreRequired)
                return PendingOwnerReconciliationResult.Unchanged(state);

            var recovery = await RestoreAndClearAsync(
                    state,
                    pendingOwner,
                    restore,
                    cancellationToken)
                .ConfigureAwait(false);
            return new PendingOwnerReconciliationResult(
                LoadPersistedStateAfterRecovery(recovery, state),
                recovery.Result,
                recovery.RestoreSucceeded);
        }

        if (packageState.Installed && !packageState.Hidden)
        {
            return PendingOwnerReconciliationResult.Failure(
                state,
                OperationResult.Failure(
                    "The visible work launch does not match the persisted pending owner."));
        }

        if (!string.IsNullOrWhiteSpace(packageState.LaunchId))
        {
            return PendingOwnerReconciliationResult.Failure(
                state,
                OperationResult.Failure(
                    "The hidden work launch does not match the persisted pending owner."));
        }

        if (state.PendingLaunchDispatched && packageState.Installed)
            return PendingOwnerReconciliationResult.Failure(state,
                OperationResult.Failure("Рабочий профиль пока не подтвердил идентификатор отправленного запуска."));

        if (!state.RestoreRequired || state.ActiveOwner is not null)
        {
            state = state.Abort(pendingOwner);
            PersistState(state);
            return PendingOwnerReconciliationResult.Unchanged(state);
        }

        state = state.Commit(pendingOwner) with { RestoreReady = true, ForceRestore = true };
        PersistState(state);

        var rollback = await RestoreAndClearAsync(state, null, restore, cancellationToken)
            .ConfigureAwait(false);
        return new PendingOwnerReconciliationResult(
            LoadPersistedStateAfterRecovery(rollback, state),
            rollback.Result,
            rollback.RestoreSucceeded);
    }

    private async Task<VpnRestoreRecoveryResult> RestoreAndClearAsync(
        VpnRestoreOwnershipState state,
        VpnRestoreOwner? ownerToConfirm,
        Func<Task<OperationResult>> restore,
        CancellationToken cancellationToken)
    {
        if (state.ActiveOwner is not null && !state.RestoreReady)
        {
            state = state with { RestoreReady = true };
            PersistState(state);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var restoreResult = await restore().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!restoreResult.Succeeded)
            return new VpnRestoreRecoveryResult(restoreResult, false);

        PersistState(state.ClearAfterRestore());
        if (ownerToConfirm is not null && _confirmRecovery is not null)
        {
            // A duplicate work callback can acknowledge an already completed owner.
            // Do not repeat VPN automation because an acknowledgement transport failed.
            try { await _confirmRecovery(ownerToConfirm, cancellationToken).ConfigureAwait(false); }
            catch (Exception) { }
        }

        return new VpnRestoreRecoveryResult(restoreResult, true);
    }

    private Task<VpnRestorePackageStateResult> QueryPackageStateAsync(
        VpnRestoreOwner owner,
        CancellationToken cancellationToken)
    {
        return _queryPackageState is null
            ? Task.FromResult(VpnRestorePackageStateResult.Failure(
                "Work package reconciliation is unavailable."))
            : _queryPackageState(owner, cancellationToken);
    }

    private VpnRestoreOwnershipState LoadPersistedStateAfterRecovery(
        VpnRestoreRecoveryResult recovery,
        VpnRestoreOwnershipState fallback)
    {
        if (!recovery.Result.Succeeded) return fallback;
        if (!TryLoadState(out var persisted, out _)) return fallback;

        return persisted;
    }

    private static bool MatchesLaunch(VpnRestoreOwner owner, string? launchId)
    {
        return !string.IsNullOrWhiteSpace(launchId)
               && string.Equals(owner.LaunchId, launchId, StringComparison.Ordinal);
    }

    private void PersistState(VpnRestoreOwnershipState state)
    {
        if (state == VpnRestoreOwnershipState.Empty)
        {
            _removeState();
            return;
        }

        _writeState(VpnRestoreOwnershipCodec.Serialize(state));
    }

    private readonly record struct PendingOwnerReconciliationResult(
        VpnRestoreOwnershipState State,
        OperationResult Result,
        bool RestoreSucceeded)
    {
        public static PendingOwnerReconciliationResult Unchanged(VpnRestoreOwnershipState state)
        {
            return new PendingOwnerReconciliationResult(
                state,
                OperationResult.Success("Pending VPN launch reconciliation completed."),
                false);
        }

        public static PendingOwnerReconciliationResult Failure(
            VpnRestoreOwnershipState state,
            OperationResult result)
        {
            return new PendingOwnerReconciliationResult(state, result, false);
        }
    }
}

internal sealed class VpnRestoreLaunchScope
{
    private readonly VpnRestoreOwner _owner;
    private readonly Func<VpnRestoreOwnershipState> _getState;
    private readonly Action<VpnRestoreOwnershipState> _saveState;
    private readonly Func<Task<OperationResult>> _restore;

    public string LaunchId => _owner.LaunchId;
    public bool HasInheritedRestoreObligation { get; }
    public bool AcquiredRestoreObligation { get; private set; }
    public bool RollbackAttempted { get; private set; }

    internal VpnRestoreLaunchScope(
        VpnRestoreOwner owner,
        bool hasInheritedRestoreObligation,
        Func<VpnRestoreOwnershipState> getState,
        Action<VpnRestoreOwnershipState> saveState,
        Func<Task<OperationResult>> restore)
    {
        _owner = owner;
        HasInheritedRestoreObligation = hasInheritedRestoreObligation;
        _getState = getState;
        _saveState = saveState;
        _restore = restore;
    }

    public void MarkRestoreRequired()
    {
        if (HasInheritedRestoreObligation) return;
        if (AcquiredRestoreObligation) return;

        _saveState(_getState().RequireRestore());
        AcquiredRestoreObligation = true;
    }

    public void MarkLaunchDispatched() => _saveState(_getState() with { PendingLaunchDispatched = true });

    public async Task<OperationResult> RollbackAsync()
    {
        if (!AcquiredRestoreObligation)
            return OperationResult.Success("VPN restore obligation was inherited or not required.");
        if (RollbackAttempted)
            return OperationResult.Failure("VPN restore rollback was already attempted.");

        RollbackAttempted = true;
        OperationResult result;
        try { result = await _restore().ConfigureAwait(false); }
        catch (Exception exception) { result = OperationResult.Failure(exception.Message); }
        if (result.Succeeded) _saveState(VpnRestoreOwnershipState.Empty);
        else _saveState(_getState() with
        {
            ActiveOwner = _owner, PendingOwner = null, RestoreReady = true, ForceRestore = true
        });
        return result;
    }
}
