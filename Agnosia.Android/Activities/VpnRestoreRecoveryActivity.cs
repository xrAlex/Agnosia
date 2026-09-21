using Agnosia.Android.Infrastructure;
using Agnosia.Android.Vpn;
using Agnosia.Models;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using OperationCanceledException = System.OperationCanceledException;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Activities;

[Activity(
    Name = "com.agnosia.app.VpnRestoreRecoveryActivity",
    Theme = "@style/Agnosia.CommandFallbackTheme",
    Exported = false,
    ExcludeFromRecents = true,
    TaskAffinity = "",
    LaunchMode = LaunchMode.SingleTask)]
public sealed class VpnRestoreRecoveryActivity : Activity, IAndroidActivityHost
{
    private const string LogTag = "AgnosiaVpnRecovery";
    private CancellationTokenSource? _flowCancellation;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<AndroidActivityResult>> _queries = new();
    private int _nextQueryCode = 8200;
    internal static IAndroidActivityHost? CurrentHost { get; private set; }

    Activity IAndroidActivityHost.CurrentActivity => this;
    Type IAndroidActivityHost.CommandActivityType => typeof(DummyActivity);
    Type IAndroidActivityHost.AdminReceiverType => typeof(Receivers.AgnosiaDeviceAdminReceiver);
    Type IAndroidActivityHost.WorkAppFrozenReceiverType => typeof(Receivers.WorkAppFrozenReceiver);
    Task<OperationResult> IAndroidActivityHost.DisconnectPreparedVpnAsync(CancellationToken token)
        => TransientVpnDisconnectService.DisconnectPreparedVpnAsync(this, token);
    void IAndroidActivityHost.ShowVpnGuardOverlay() => OverlayVpnService.ShowOverlay(this);
    void IAndroidActivityHost.HideVpnGuardOverlay() => OverlayVpnService.HideOverlay(this);

    async Task<AndroidActivityResult> IAndroidActivityHost.StartForResultAsync(Intent intent, CancellationToken token,
        Action? beforeStart)
    {
        var requestCode = Interlocked.Increment(ref _nextQueryCode);
        var completion = new TaskCompletionSource<AndroidActivityResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queries[requestCode] = completion;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token,
            _flowCancellation?.Token ?? CancellationToken.None);
        try
        {
            RunOnUiThread(() =>
            {
                try
                {
                    linked.Token.ThrowIfCancellationRequested();
                    beforeStart?.Invoke();
                    StartActivityForResult(intent, requestCode);
                }
                catch (Exception exception) { completion.TrySetException(exception); }
            });
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(30), linked.Token).ConfigureAwait(false);
        }
        finally { _queries.TryRemove(requestCode, out _); }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (_queries.TryRemove(requestCode, out var completion))
            completion.TrySetResult(new AndroidActivityResult(resultCode, data));
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        AgnosiaRuntime.Initialize(this);
        CurrentHost = this;
        _flowCancellation = new CancellationTokenSource();
        _ = RecoverAsync(_flowCancellation.Token);
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(CurrentHost, this)) CurrentHost = null;
        _flowCancellation?.Cancel();
        _flowCancellation?.Dispose();
        _flowCancellation = null;
        base.OnDestroy();
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        var packageName = Intent?.GetStringExtra(VpnRestoreRetryScheduler.ExtraPackageName)
                          ?? PackageName
                          ?? "com.agnosia.app";
        var launchId = Intent?.GetStringExtra(VpnRestoreRetryScheduler.ExtraLaunchId);
        var afterDeviceRestart = Intent?.GetBooleanExtra(
            VpnRestoreRetryScheduler.ExtraAfterDeviceRestart,
            false) == true;
        OperationResult result;
        try
        {
            // Retain the next attempt before calling a VPN client that may stop this process.
            var attempt = Math.Clamp(Intent?.GetIntExtra(VpnRestoreRetryScheduler.ExtraAttempt, 0) ?? 0, 0, 4) + 1;
            VpnRestoreRetryScheduler.Schedule(this, typeof(VpnRestoreRecoveryActivity), packageName,
                launchId, attempt, afterDeviceRestart);
            result = afterDeviceRestart
                ? await WorkAppFrozenHandler.RecoverAfterDeviceRestartAndHideOverlayAsync(
                        this,
                        string.IsNullOrWhiteSpace(launchId) ? null : new VpnRestoreOwner(launchId, packageName),
                        "device_restart_recovery",
                        LogTag,
                        cancellationToken)
                    .ConfigureAwait(false)
                : await ReconcileAndRestoreAsync(packageName, launchId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Scheduled VPN restore failed: {exception}");
            result = OperationResult.Failure("Не удалось повторить восстановление VPN.");
        }

        if (!result.Succeeded)
        {
            var attempt = Math.Max(0, Intent?.GetIntExtra(VpnRestoreRetryScheduler.ExtraAttempt, 0) ?? 0) + 1;
            VpnRestoreRetryScheduler.Schedule(
                this,
                typeof(VpnRestoreRecoveryActivity),
                packageName,
                launchId,
                attempt,
                afterDeviceRestart);
        }
        else VpnRestoreRetryScheduler.Cancel(this, packageName, launchId);

        RunOnUiThread(Finish);
    }

    private async Task<OperationResult> ReconcileAndRestoreAsync(string packageName, string? launchId,
        CancellationToken cancellationToken)
    {
        var coordinator = ServiceRegistry.GetRequiredService<VpnRestoreOwnershipCoordinator>();
        var recovery = await coordinator.RecoverAsync(
            () => WorkAppFrozenHandler.RestoreOwnedVpnAsync(this, "scheduled_restore_retry"), cancellationToken,
            string.IsNullOrWhiteSpace(launchId) ? null : new VpnRestoreOwner(launchId, packageName)).ConfigureAwait(false);
        if (recovery.RestoreSucceeded) WorkAppFrozenHandler.HideOverlay(this, LogTag);
        var storage = ServiceRegistry.GetRequiredService<LocalStorageManager>();
        if (VpnRestoreOwnershipCodec.TryDeserialize(storage.GetString(StorageKeys.VpnRestoreOwnershipState), out var state)
            && state.RestoreRequired && (state.PendingOwner ?? state.ActiveOwner) is { } owner
            && owner.LaunchId == launchId && owner.PackageName == packageName)
            return OperationResult.Failure("Рабочая сессия ещё активна или ожидает сверки; восстановление отложено.");
        return recovery.Result;
    }
}
