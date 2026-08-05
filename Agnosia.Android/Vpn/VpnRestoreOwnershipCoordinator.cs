using Agnosia.Models;

namespace Agnosia.Android.Vpn;

internal readonly record struct VpnRestoreCompletionResult(
    OperationResult Result,
    bool OwnerMatched);

internal sealed class VpnRestoreOwnershipCoordinator
{
    private readonly Func<string?> _readState;
    private readonly Action<string> _writeState;
    private readonly Action _removeState;
    private readonly Func<bool> _readLegacyFlag;
    private readonly Action _clearLegacyFlag;
    private readonly Func<string> _createLaunchId;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public VpnRestoreOwnershipCoordinator(
        Func<string?> readState,
        Action<string> writeState,
        Action removeState,
        Func<bool> readLegacyFlag,
        Action clearLegacyFlag,
        Func<string>? createLaunchId = null)
    {
        _readState = readState ?? throw new ArgumentNullException(nameof(readState));
        _writeState = writeState ?? throw new ArgumentNullException(nameof(writeState));
        _removeState = removeState ?? throw new ArgumentNullException(nameof(removeState));
        _readLegacyFlag = readLegacyFlag ?? throw new ArgumentNullException(nameof(readLegacyFlag));
        _clearLegacyFlag = clearLegacyFlag ?? throw new ArgumentNullException(nameof(clearLegacyFlag));
        _createLaunchId = createLaunchId ?? (() => Guid.NewGuid().ToString("N"));
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

    private void PersistState(VpnRestoreOwnershipState state)
    {
        if (state == VpnRestoreOwnershipState.Empty)
        {
            _removeState();
            return;
        }

        _writeState(VpnRestoreOwnershipCodec.Serialize(state));
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

    public async Task<OperationResult> RollbackAsync()
    {
        if (!AcquiredRestoreObligation)
            return OperationResult.Success("VPN restore obligation was inherited or not required.");
        if (RollbackAttempted)
            return OperationResult.Failure("VPN restore rollback was already attempted.");

        RollbackAttempted = true;
        var result = await _restore().ConfigureAwait(false);
        if (result.Succeeded) _saveState(VpnRestoreOwnershipState.Empty);
        return result;
    }
}
