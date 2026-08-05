using Agnosia.Android.Infrastructure;
using Agnosia.Android.Receivers;
using Agnosia.Android.Services;
using Agnosia.Models;
using Android.App.Admin;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Exception = System.Exception;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Activities;

[Activity(
    Name = "com.agnosia.app.ProxyActivity",
    Theme = "@style/Agnosia.ProxyTheme",
    Exported = true,
    ExcludeFromRecents = true,
    NoHistory = true,
    TaskAffinity = "",
    LaunchMode = LaunchMode.SingleTask)]
[IntentFilter(
[
    AgnosiaActions.LaunchAppProxy
], Categories = [Intent.CategoryDefault])]
public sealed class ProxyActivity : Activity
{
    private const string LogTag = "AgnosiaProxyActivity";
    private const int PrepareVpnRequestCode = 7100;
    private const int LaunchResolveAttempts = 12;
    private const int LaunchResolveDelayMilliseconds = 120;
    private static readonly TimeSpan WorkLaunchTimeout = TimeSpan.FromSeconds(30);

    private bool _launchStarted;
    private HiddenAppLaunchRequest? _request;
    private IReadOnlySet<long> _vpnDisconnectBaseline = new HashSet<long>();
    private AndroidAppLaunchResult? _launchResult;
    private CancellationTokenSource _flowCts = new();
    private TaskCompletionSource<Result>? _pendingVpnPreparation;
    private TaskCompletionSource<AndroidActivityResult>? _pendingWorkLaunch;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        AgnosiaRuntime.Initialize(this);
        TryStartProxyFlow();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent is null) return;

        CancelPendingFlow();
        Intent = intent;
        _launchStarted = false;
        _request = null;
        _vpnDisconnectBaseline = new HashSet<long>();
        _launchResult = null;
        TryStartProxyFlow();
    }

    protected override void OnDestroy()
    {
        CancelPendingFlow();
        _flowCts.Dispose();
        base.OnDestroy();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        Log.Debug(
            LogTag,
            $"Proxy activity result received. requestCode={requestCode}, result={resultCode}, activePackage={_request?.PackageName ?? "<none>"}, hasData={data is not null}.");

        if (requestCode == PrepareVpnRequestCode && _pendingVpnPreparation is not null)
        {
            _pendingVpnPreparation?.TrySetResult(resultCode);
            return;
        }

        _pendingWorkLaunch?.TrySetResult(new AndroidActivityResult(resultCode, data));
    }

    private void TryStartProxyFlow()
    {
        if (_launchStarted) return;

        if (!HiddenAppShortcutManager.TryGetLaunchRequest(Intent, out var request))
        {
            Log.Warn(LogTag, $"Proxy launch request rejected. action={Intent?.Action ?? "<none>"}.");
            var rejectedResult = AndroidAppLaunchResult.TryRead(Intent, out var existingResult)
                ? existingResult
                : AndroidAppLaunchResult.CommandReceived(null, null);
            FinishWithLaunchResult(
                rejectedResult.Fail(
                    AndroidAppLaunchStage.CommandReceived,
                    AndroidAppLaunchIssueKind.InvalidRequest,
                    "proxy_request_rejected"),
                false);
            return;
        }

        Log.Debug(
            LogTag,
            $"Proxy launch request accepted. package={request.PackageName}, targetActivity={request.TargetActivity ?? "<none>"}, displayName={request.DisplayName}.");
        _launchStarted = true;
        _request = request;
        _launchResult = (AndroidAppLaunchResult.TryRead(Intent, out var launchResult)
                ? launchResult
                : AndroidAppLaunchResult.CommandReceived(request.PackageName, request.DisplayName))
            .WithDisplayName(request.DisplayName);

        RunInBackground(
            () => UnhideAndLaunchAsync(request, _flowCts.Token),
            $"Android не смог подготовить {request.DisplayName} к запуску.");
    }

    private async Task UnhideAndLaunchAsync(
        HiddenAppLaunchRequest request,
        CancellationToken cancellationToken)
    {
        TemporaryPackageVisibilityTransaction? visibilityTransaction = null;
        try
        {
            if (!AgnosiaUtilities.IsProfileOwner(this))
            {
                await PrepareVpnIfNeededAndForwardAsync(request, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (AndroidSystemApi.GetDevicePolicyManager(this) is not { } policyManager)
            {
                FinishWithLaunchResult(
                    GetLaunchResult(request).Fail(
                        AndroidAppLaunchStage.CommandReceived,
                        AndroidAppLaunchIssueKind.DevicePolicyManagerUnavailable,
                        "devicePolicyManager=missing"),
                    true);
                return;
            }

            var admin = AgnosiaUtilities.GetAdminComponent(this, typeof(AgnosiaDeviceAdminReceiver));
            if (IsSystemWorkProfileRequest(request))
            {
                Log.Info(LogTag, $"Launching system work-profile app without hidden-session monitor. package={request.PackageName}.");
                LaunchVisibleSystemPackage(request);
                return;
            }

            var launchResult = GetLaunchResult(request);
            var preflight = HiddenAppLaunchPreflight.RequireUsageAccess(
                launchResult,
                AndroidUsageStatsAccessApi.HasAccess(this, LogTag, false));
            if (!preflight.CanProceed)
            {
                FinishWithLaunchResult(preflight.LaunchResult, true);
                return;
            }

            launchResult = preflight.LaunchResult;
            bool wasHidden;
            try
            {
                wasHidden = policyManager.IsApplicationHidden(admin, request.PackageName);
            }
            catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
            {
                FinishWithLaunchResult(
                    launchResult.Fail(
                        AndroidAppLaunchStage.CommandReceived,
                        AndroidAppLaunchIssueKind.HiddenOrSuspendedPackageState,
                        $"isApplicationHidden=unavailable:{exception.GetType().Name}"),
                    true);
                return;
            }

            visibilityTransaction = new TemporaryPackageVisibilityTransaction(wasHidden);
            if (wasHidden)
            {
                if (!AndroidPolicyApi.TrySetApplicationHidden(
                        policyManager,
                        admin,
                        request.PackageName,
                        false,
                        LogTag,
                        out var error))
                {
                    FinishWithLaunchResult(
                        launchResult.Fail(
                            AndroidAppLaunchStage.CommandReceived,
                            AndroidAppLaunchIssueKind.HiddenOrSuspendedPackageState,
                            "setApplicationHidden=false failed",
                            error),
                        true);
                    return;
                }

                visibilityTransaction.MarkPackageUnhidden();
                launchResult = launchResult.WithStage(AndroidAppLaunchStage.PackageUnhidden);
            }

            _launchResult = launchResult;
            if (TryGetPackageLaunchBlockIssue(policyManager, admin, request.PackageName, out var blockDetail) is
                { } blockIssue)
            {
                var failedResult = launchResult.Fail(
                    AndroidAppLaunchStage.PackageUnhidden,
                    blockIssue,
                    blockDetail);
                failedResult = TryHideImmediately(
                    request,
                    "package_blocked",
                    failedResult,
                    visibilityTransaction);
                FinishWithLaunchResult(
                    failedResult,
                    true);
                return;
            }

            if (PackageManager is null)
            {
                var failedResult = launchResult.Fail(
                    AndroidAppLaunchStage.PackageUnhidden,
                    AndroidAppLaunchIssueKind.PackageManagerUnavailable,
                    "packageManager=missing");
                failedResult = TryHideImmediately(
                    request,
                    "package_manager_missing",
                    failedResult,
                    visibilityTransaction);
                FinishWithLaunchResult(
                    failedResult,
                    true);
                return;
            }

            await RefreshLockdownForUnhiddenPackageAsync(policyManager, admin, request.PackageName)
                .ConfigureAwait(false);

            Intent? launchIntent = null;
            for (var attempt = 0; attempt < LaunchResolveAttempts; attempt++)
            {
                launchIntent = CreateLaunchIntent(request);
                if (launchIntent is not null) break;

                await Task.Delay(LaunchResolveDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            }

            if (launchIntent is null)
            {
                var issue = TryGetPackageLaunchBlockIssue(policyManager, admin, request.PackageName, out blockDetail)
                            ?? AndroidAppLaunchIssueKind.MissingLauncherActivity;
                var failedResult = launchResult.Fail(
                    AndroidAppLaunchStage.PackageUnhidden,
                    issue,
                    blockDetail ?? "launchIntent=null");
                failedResult = TryHideImmediately(
                    request,
                    "launch_intent_missing",
                    failedResult,
                    visibilityTransaction);
                FinishWithLaunchResult(
                    failedResult,
                    true);
                return;
            }

            launchResult = launchResult.WithStage(
                AndroidAppLaunchStage.LaunchIntentResolved,
                $"component={launchIntent.Component?.FlattenToShortString() ?? "<none>"}");
            _launchResult = launchResult;
            Log.Debug(
                LogTag,
                $"Resolved launch intent for {request.PackageName}. component={launchIntent.Component?.FlattenToShortString() ?? "<none>"}, flags={launchIntent.Flags}.");

            var resultToStart = launchResult;
            RunOnUiThread(() =>
            {
                try
                {
                    StartActivity(launchIntent);
                    var startedResult = resultToStart.WithStage(AndroidAppLaunchStage.StartActivityAttempted);
                    _launchResult = startedResult;
                    Log.Debug(
                        LogTag,
                        $"StartActivity returned for {request.PackageName}. component={launchIntent.Component?.FlattenToShortString() ?? "<none>"}, flags={launchIntent.Flags}, proxyTaskId={TaskId}.");
                    Log.Debug(LogTag, $"Starting hidden-session monitor for {request.PackageName}, taskId={TaskId}.");
                    if (!HiddenAppSessionMonitorService.StartMonitoring(
                            this,
                            request.PackageName,
                            request.DisplayName,
                            TaskId,
                            startedResult,
                            AndroidIntentExtras.ReadParentFrozenCallback(Intent)))
                        throw new InvalidOperationException(
                            $"Android did not accept the hidden-session monitor for {request.PackageName}.");
                    visibilityTransaction.Commit();
                    Log.Debug(LogTag, $"Monitor service request sent for {request.PackageName}.");
                    FinishWithLaunchResult(startedResult, false);
                }
                catch (ActivityNotFoundException exception)
                {
                    var failedResult = resultToStart.Fail(
                        AndroidAppLaunchStage.StartActivityFailedWithException,
                        AndroidAppLaunchIssueKind.MissingLauncherActivity,
                        exception.ToString());
                    failedResult = TryHideImmediately(
                        request,
                        "activity_not_found",
                        failedResult,
                        visibilityTransaction);
                    FinishWithLaunchResult(failedResult, true);
                }
                catch (Exception exception)
                {
                    Log.Error(LogTag, $"Failed to launch {request.PackageName}: {exception}");
                    var failedResult = resultToStart.Fail(
                        AndroidAppLaunchStage.StartActivityFailedWithException,
                        AndroidAppLaunchResult.ClassifyStartActivityException(exception),
                        exception.ToString());
                    failedResult = TryHideImmediately(
                        request,
                        "launch_failed",
                        failedResult,
                        visibilityTransaction);
                    FinishWithLaunchResult(failedResult, true);
                }
            });
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Proxy flow failed for {request.PackageName}: {exception}");
            var failedResult = GetLaunchResult(request).Fail(
                AndroidAppLaunchStage.CommandReceived,
                AndroidAppLaunchResult.ClassifyStartActivityException(exception),
                exception.ToString(),
                $"Android не смог подготовить {request.DisplayName} к запуску.");
            failedResult = TryHideImmediately(
                request,
                "proxy_flow_failed",
                failedResult,
                visibilityTransaction);
            FinishWithLaunchResult(
                failedResult,
                true);
        }
    }

    private async Task PrepareVpnIfNeededAndForwardAsync(
        HiddenAppLaunchRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (WorkProfileLaunchPreflight.TryCreateFailure(this, GetLaunchResult(request)) is { } preflightFailure)
            {
                preflightFailure.Log(LogTag);
                FinishWithLaunchResult(preflightFailure, true);
                return;
            }

            if (IsSystemWorkProfileRequest(request))
            {
                Log.Debug(LogTag, $"Shortcut launch: skipping VPN Guard for system work-profile app {request.PackageName}.");
                var systemLaunchResult = await ForwardLaunchToManagedProfileAsync(
                        request,
                        isSystem: true,
                        launchId: null,
                        cancellationToken)
                    .ConfigureAwait(false);
                FinishForwardedLaunch(systemLaunchResult);
                return;
            }

            var vpnRestoreOwnershipCoordinator =
                ServiceRegistry.GetRequiredService<VpnRestoreOwnershipCoordinator>();
            var launchResult = await vpnRestoreOwnershipCoordinator.ExecuteLaunchAsync(
                    request.PackageName,
                    (scope, token) => WorkLaunchVpnTransaction.ExecuteAsync(
                        _ => Task.FromResult(OperationResult.Success(string.Empty)),
                        takeoverToken => PrepareShortcutVpnTakeoverAsync(scope, takeoverToken),
                        launchToken => ForwardLaunchToManagedProfileAfterPreflightAsync(
                            request,
                            scope.LaunchId,
                            launchToken),
                        scope.RollbackAsync,
                        () => scope.AcquiredRestoreObligation,
                        token),
                    () => WorkAppFrozenHandler.RollbackFailedWorkLaunchAsync(
                        this,
                        $"shortcut_launch_rollback:{request.PackageName}",
                        LogTag),
                    cancellationToken)
                .ConfigureAwait(false);
            FinishForwardedLaunch(launchResult);
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Failed to prepare VPN disconnect for shortcut launch: {exception}");
            ShowErrorAndFinish("Agnosia не смог отключить VPN перед запуском ярлыка.");
        }
    }

    private async Task<WorkLaunchVpnTakeoverResult> PrepareShortcutVpnTakeoverAsync(
        VpnRestoreLaunchScope scope,
        CancellationToken cancellationToken)
    {
        var storage = ServiceRegistry.GetRequiredService<LocalStorageManager>();
        if (scope.HasInheritedRestoreObligation)
        {
            Log.Info(LogTag, "Shortcut launch inherited VPN restore ownership.");
            return WorkLaunchVpnTakeoverResult.NotRequired(
                OperationResult.Success("VPN уже отключен предыдущей сессией Agnosia."));
        }

        if (!storage.GetBoolean(StorageKeys.DisableVpnBeforeWorkLaunch))
        {
            Log.Debug(LogTag, "Disable-VPN-before-shortcut-launch is disabled in settings.");
            return WorkLaunchVpnTakeoverResult.NotRequired(OperationResult.Success(string.Empty));
        }

        Log.Info(LogTag, $"VPN Guard is enabled for shortcut launch. package={_request?.PackageName ?? "<none>"}.");
        if (!AndroidVpnApi.IsVpnActive(this))
        {
            Log.Info(LogTag, "Shortcut launch: no active VPN detected.");
            return WorkLaunchVpnTakeoverResult.NotRequired(OperationResult.Success(string.Empty));
        }

        scope.MarkRestoreRequired();
        var prepareIntent = VpnService.Prepare(this);
        if (prepareIntent is not null)
        {
            Log.Info(LogTag, "Shortcut launch: Android confirmation is required for VPN control.");
            var resultCode = await RequestVpnPreparationAsync(prepareIntent, cancellationToken).ConfigureAwait(false);
            if (resultCode != Result.Ok)
                return WorkLaunchVpnTakeoverResult.Acquired(
                    OperationResult.Failure("Android не выдал Agnosia временное управление VPN."));
        }

        if (!AndroidVpnApi.IsVpnActive(this))
        {
            OverlayVpnService.ShowOverlay(this);
            Log.Debug(LogTag, "Shortcut launch: active VPN was cleared while preparing VPN control.");
            return WorkLaunchVpnTakeoverResult.Acquired(OperationResult.Success("VPN отключен."));
        }

        _vpnDisconnectBaseline = AndroidVpnApi.GetVisibleVpnNetworkHandles(this);
        var result = await TransientVpnDisconnectService.DisconnectPreparedVpnAsync(this, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded)
            return WorkLaunchVpnTakeoverResult.Acquired(result);

        if (AndroidVpnApi.IsVpnActive(this, _vpnDisconnectBaseline))
            return WorkLaunchVpnTakeoverResult.Acquired(
                OperationResult.Failure(
                    "VPN все еще активен в личном профиле. Сторонний клиент мог сразу подключиться снова."));

        OverlayVpnService.ShowOverlay(this);
        return WorkLaunchVpnTakeoverResult.Acquired(OperationResult.Success("VPN отключен."));
    }

    private Task<OperationResult> ForwardLaunchToManagedProfileAfterPreflightAsync(
        HiddenAppLaunchRequest request,
        string launchId,
        CancellationToken cancellationToken)
    {
        var failure = WorkProfileLaunchPreflight.TryCreateFailure(this, GetLaunchResult(request));
        return failure is null
            ? ForwardLaunchToManagedProfileAsync(request, isSystem: false, launchId, cancellationToken)
            : Task.FromResult(failure.ToOperationResult());
    }

    private void LaunchVisibleSystemPackage(HiddenAppLaunchRequest request)
    {
        var launchIntent = CreateLaunchIntent(request);
        if (launchIntent is null)
        {
            FinishWithLaunchResult(
                GetLaunchResult(request).Fail(
                    AndroidAppLaunchStage.CommandReceived,
                    AndroidAppLaunchIssueKind.MissingLauncherActivity,
                    "system_work_app_launchIntent=null"),
                true);
            return;
        }

        RunOnUiThread(() =>
        {
            try
            {
                StartActivity(launchIntent);
                FinishWithLaunchResult(
                    GetLaunchResult(request).WithStage(
                        AndroidAppLaunchStage.StartActivityAttempted,
                        "system_work_app_direct_launch"),
                    false);
            }
            catch (Exception exception)
            {
                FinishWithLaunchResult(
                    GetLaunchResult(request).Fail(
                        AndroidAppLaunchStage.StartActivityFailedWithException,
                        AndroidAppLaunchResult.ClassifyStartActivityException(exception),
                        exception.ToString()),
                    true);
            }
        });
    }

    private void RunInBackground(Func<Task> operation, string userFailureMessage)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await operation().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Log.Error(LogTag, $"Background proxy operation failed: {exception}");
                ShowErrorAndFinish(userFailureMessage);
            }
        });
    }

    private async Task<Result> RequestVpnPreparationAsync(
        Intent prepareIntent,
        CancellationToken cancellationToken)
    {
        if (_pendingVpnPreparation is not null)
            throw new InvalidOperationException("A VPN preparation request is already pending.");

        var completionSource = new TaskCompletionSource<Result>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingVpnPreparation = completionSource;
        RunOnUiThread(() =>
        {
            try
            {
                StartActivityForResult(prepareIntent, PrepareVpnRequestCode);
            }
            catch (Exception exception)
            {
                completionSource.TrySetException(exception);
            }
        });

        try
        {
            return await completionSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (ReferenceEquals(_pendingVpnPreparation, completionSource))
                _pendingVpnPreparation = null;
        }
    }

    private async Task<OperationResult> ForwardLaunchToManagedProfileAsync(
        HiddenAppLaunchRequest request,
        bool isSystem,
        string? launchId,
        CancellationToken cancellationToken)
    {
        if (_pendingWorkLaunch is not null)
            return OperationResult.Failure("Запуск другого рабочего приложения уже ожидает подтверждения.");

        var crossProfileApps = AndroidSystemApi.GetCrossProfileApps(this);
        if (crossProfileApps is null || !crossProfileApps.CanInteractAcrossProfiles())
            return OperationResult.Failure("Agnosia не разрешено напрямую обращаться к рабочему профилю.");

        var targetUser = crossProfileApps.TargetUserProfiles
            .OfType<UserHandle>()
            .FirstOrDefault();
        if (targetUser is null)
            return OperationResult.Failure("Android не нашёл доступный рабочий профиль Agnosia.");

        var proxyIntent = HiddenAppShortcutManager.CreateInternalLaunchIntent(
            request.PackageName,
            request.TargetActivity,
            request.DisplayName);
        var isSystemLaunch = isSystem || request.IsSystem;
        proxyIntent.PutExtra(AndroidCommandContract.ExtraIsSystem, isSystemLaunch);
        if (!isSystemLaunch)
        {
            if (string.IsNullOrWhiteSpace(launchId))
                return OperationResult.Failure("Agnosia не создала идентификатор восстановления VPN.");
            proxyIntent.PutExtra(
                AndroidCommandContract.ExtraParentFrozenCallback,
                AgnosiaPendingIntentFactory.CreateWorkAppFrozenBroadcastPendingIntent(
                    this,
                    typeof(WorkAppFrozenReceiver),
                    request.PackageName,
                    launchId));
        }
        proxyIntent.SetComponent(
            new ComponentName(this, Java.Lang.Class.FromType(typeof(ProxyActivity))));
        AuthenticationUtility.SignIntent(proxyIntent);

        var completionSource = new TaskCompletionSource<AndroidActivityResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingWorkLaunch = completionSource;
        RunOnUiThread(() =>
        {
            try
            {
                Log.Debug(
                    LogTag,
                    $"Starting shortcut work launch for result. package={request.PackageName}, target={targetUser}.");
                crossProfileApps.StartActivity(proxyIntent, targetUser, this);
            }
            catch (Exception exception)
            {
                Log.Warn(LogTag, $"Failed to start shortcut work launch: {exception}");
                completionSource.TrySetException(exception);
            }
        });

        try
        {
            var activityResult = await completionSource.Task
                .WaitAsync(WorkLaunchTimeout, cancellationToken)
                .ConfigureAwait(false);
            return AndroidActivityResultApi.ToVoidOperationResult(activityResult, "Открываем приложение.");
        }
        catch (TimeoutException)
        {
            return OperationResult.Failure("Рабочий профиль не подтвердил запуск приложения вовремя.");
        }
        finally
        {
            if (ReferenceEquals(_pendingWorkLaunch, completionSource))
                _pendingWorkLaunch = null;
        }
    }

    private void FinishForwardedLaunch(OperationResult result)
    {
        if (result.Succeeded)
        {
            RunOnUiThread(Finish);
            return;
        }

        ShowErrorAndFinish(result.Message);
    }

    private void CancelPendingFlow()
    {
        _flowCts.Cancel();
        _pendingVpnPreparation?.TrySetCanceled(_flowCts.Token);
        _pendingWorkLaunch?.TrySetCanceled(_flowCts.Token);
        _pendingVpnPreparation = null;
        _pendingWorkLaunch = null;
        _flowCts.Dispose();
        _flowCts = new CancellationTokenSource();
    }

    private Intent? CreateLaunchIntent(HiddenAppLaunchRequest request)
    {
        var launchIntent = PackageManager?.GetLaunchIntentForPackage(request.PackageName);
        if (launchIntent is null) return null;

        if (!string.IsNullOrWhiteSpace(request.TargetActivity))
            launchIntent.SetComponent(new ComponentName(request.PackageName, request.TargetActivity));

        const ActivityFlags flagsToClear = ActivityFlags.NoAnimation;
        launchIntent.SetFlags(launchIntent.Flags & ~flagsToClear);
        launchIntent.AddFlags(
            ActivityFlags.NewTask
            | ActivityFlags.ResetTaskIfNeeded
            | ActivityFlags.ClearTop
            | ActivityFlags.SingleTop);
        return launchIntent;
    }

    private AndroidAppLaunchResult TryHideImmediately(
        HiddenAppLaunchRequest request,
        string reason,
        AndroidAppLaunchResult launchResult,
        TemporaryPackageVisibilityTransaction? visibilityTransaction)
    {
        if (visibilityTransaction?.RollbackRequired != true) return launchResult;

        try
        {
            if (!AgnosiaUtilities.IsProfileOwner(this)
                || AndroidSystemApi.GetDevicePolicyManager(this) is not { } policyManager)
                return launchResult;

            if (IsSystemWorkProfileRequest(request))
                return launchResult;

            var admin = AgnosiaUtilities.GetAdminComponent(this, typeof(AgnosiaDeviceAdminReceiver));
            if (AndroidPolicyApi.TrySetApplicationHidden(
                    policyManager,
                    admin,
                    request.PackageName,
                    true,
                    LogTag,
                    out _))
            {
                Log.Info(LogTag, $"App {request.PackageName} hidden again directly. reason={reason}");
                visibilityTransaction.Commit();
                launchResult = launchResult.WithStage(
                    AndroidAppLaunchStage.PackageRehidden,
                    $"proxy_fallback:{reason}");
                launchResult.Log(LogTag);
                var result = AndroidProfileCommandGateway.NotifyParentWorkAppFrozen(
                    this,
                    request.PackageName,
                    $"proxy_fallback:{reason}:{request.PackageName}");
                if (!result.Succeeded)
                    Log.Warn(LogTag,
                        $"Could not notify parent profile about fallback freeze for {request.PackageName}: {result.Message}");
            }
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Fallback re-hide for {request.PackageName} failed: {exception}");
        }

        return launchResult;
    }

    private async Task RefreshLockdownForUnhiddenPackageAsync(
        DevicePolicyManager policyManager,
        ComponentName admin,
        string packageName)
    {
        if (!LockdownSettingsStore.IsEnabled()) return;
        if (IsLockdownBlockedPackage(packageName)) return;
        if (!await WaitForPackageVisibleToVpnPolicyAsync(packageName).ConfigureAwait(false)) return;

        var result = LockdownVpnController.RefreshPolicy(this, policyManager, admin);
        if (!result.Succeeded)
            Log.Warn(LogTag, $"Lockdown policy refresh after unhide failed for {packageName}: {result.Message}");
    }

    private static bool IsLockdownBlockedPackage(string packageName)
    {
        var blockedPackages = LockdownSettingsStore.LoadBlockedPackages();
        return blockedPackages.Contains(packageName, StringComparer.Ordinal);
    }

    private async Task<bool> WaitForPackageVisibleToVpnPolicyAsync(string packageName)
    {
        for (var attempt = 0; attempt < LaunchResolveAttempts; attempt++)
        {
            if (IsPackageVisibleToVpnPolicy(packageName)) return true;
            await Task.Delay(LaunchResolveDelayMilliseconds).ConfigureAwait(false);
        }

        Log.Warn(
            LogTag,
            $"Lockdown policy refresh skipped because package is not visible after unhide. package={packageName}.");
        return false;
    }

    private bool IsPackageVisibleToVpnPolicy(string packageName)
    {
        try
        {
            var packageInfo = PackageManager?.GetPackageInfo(packageName, PackageInfoFlags.MatchDisabledComponents);
            return packageInfo?.ApplicationInfo is { } appInfo
                   && (appInfo.Flags & ApplicationInfoFlags.Installed) != 0;
        }
        catch (Exception exception) when (exception is PackageManager.NameNotFoundException
                                          || AndroidRecoverableException.IsMatch(exception))
        {
            return false;
        }
    }

    private void ShowErrorAndFinish(string message)
    {
        Log.Warn(LogTag,
            $"Finishing proxy flow with error. package={_request?.PackageName ?? "<none>"}, message={message}");
        RunOnUiThread(() =>
        {
            Toast.MakeText(this, message, ToastLength.Long)?.Show();
            Finish();
        });
    }

    private AndroidAppLaunchResult GetLaunchResult(HiddenAppLaunchRequest request)
    {
        return (_launchResult ?? AndroidAppLaunchResult.CommandReceived(request.PackageName, request.DisplayName))
            .WithDisplayName(request.DisplayName);
    }

    private bool IsSystemWorkProfileRequest(HiddenAppLaunchRequest request)
    {
        return request.IsSystem
               || AndroidWorkProfilePackageClassifier.IsSystemPackage(PackageManager, request.PackageName);
    }

    private void FinishWithLaunchResult(AndroidAppLaunchResult result, bool showToast)
    {
        _launchResult = result;
        result.Log(LogTag);

        if (Looper.MainLooper?.IsCurrentThread == true)
        {
            FinishCore();
            return;
        }

        RunOnUiThread(FinishCore);
        return;

        void FinishCore()
        {
            if (showToast || !result.Succeeded) Toast.MakeText(this, result.Message, ToastLength.Long)?.Show();

            SetResult(result.Succeeded ? Result.Ok : Result.Canceled, result.ToIntent());
            Finish();
        }
    }

    private static AndroidAppLaunchIssueKind? TryGetPackageLaunchBlockIssue(
        DevicePolicyManager policyManager,
        ComponentName admin,
        string packageName,
        out string? detail)
    {
        try
        {
            if (policyManager.IsApplicationHidden(admin, packageName))
            {
                detail = "packageHidden=true";
                return AndroidAppLaunchIssueKind.HiddenOrSuspendedPackageState;
            }

            if (policyManager.IsPackageSuspended(admin, packageName))
            {
                detail = "packageSuspended=true";
                return AndroidAppLaunchIssueKind.HiddenOrSuspendedPackageState;
            }
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            detail = $"packageState=unavailable:{exception.GetType().Name}";
            return null;
        }

        detail = null;
        return null;
    }
}
