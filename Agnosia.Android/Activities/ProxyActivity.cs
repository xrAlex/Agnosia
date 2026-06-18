using Agnosia.Android.Infrastructure;
using Agnosia.Android.Receivers;
using Agnosia.Android.Services;
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
    private const int LaunchRequestCode = 7101;
    private const int LaunchResolveAttempts = 12;
    private const int LaunchResolveDelayMilliseconds = 120;

    private bool _launchStarted;
    private bool _rehideStarted;
    private HiddenAppLaunchRequest? _request;
    private HiddenAppLaunchRequest? _pendingVpnDisconnectRequest;
    private AndroidAppLaunchResult? _launchResult;

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

        Intent = intent;
        _launchStarted = false;
        _rehideStarted = false;
        _request = null;
        _pendingVpnDisconnectRequest = null;
        _launchResult = null;
        TryStartProxyFlow();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        Log.Debug(
            LogTag,
            $"Proxy activity result received. requestCode={requestCode}, result={resultCode}, activePackage={_request?.PackageName ?? "<none>"}, hasData={data is not null}.");

        if (requestCode == PrepareVpnRequestCode)
        {
            if (_pendingVpnDisconnectRequest is not { } vpnRequest)
            {
                Finish();
                return;
            }

            if (resultCode != Result.Ok)
            {
                ShowErrorAndFinish("Android не выдал Agnosia временное управление VPN.");
                return;
            }

            RunInBackground(
                () => DisconnectVpnAndForwardAsync(vpnRequest),
                "Agnosia не смог отключить VPN перед запуском ярлыка.");
            return;
        }

        if (requestCode != LaunchRequestCode || _request is null) return;

        RehideAndFinish(_request);
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
            () => UnhideAndLaunchAsync(request),
            $"Android не смог подготовить {request.DisplayName} к запуску.");
    }

    private async Task UnhideAndLaunchAsync(HiddenAppLaunchRequest request)
    {
        try
        {
            if (!AgnosiaUtilities.IsProfileOwner(this))
            {
                await PrepareVpnIfNeededAndForwardAsync(request).ConfigureAwait(false);
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

            launchResult = launchResult.WithStage(AndroidAppLaunchStage.PackageUnhidden);
            _launchResult = launchResult;
            if (TryGetPackageLaunchBlockIssue(policyManager, admin, request.PackageName, out var blockDetail) is
                { } blockIssue)
            {
                FinishWithLaunchResult(
                    launchResult.Fail(
                        AndroidAppLaunchStage.PackageUnhidden,
                        blockIssue,
                        blockDetail),
                    true);
                return;
            }

            if (PackageManager is null)
            {
                FinishWithLaunchResult(
                    launchResult.Fail(
                        AndroidAppLaunchStage.PackageUnhidden,
                        AndroidAppLaunchIssueKind.PackageManagerUnavailable,
                        "packageManager=missing"),
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

                await Task.Delay(LaunchResolveDelayMilliseconds).ConfigureAwait(false);
            }

            if (launchIntent is null)
            {
                var issue = TryGetPackageLaunchBlockIssue(policyManager, admin, request.PackageName, out blockDetail)
                            ?? AndroidAppLaunchIssueKind.MissingLauncherActivity;
                FinishWithLaunchResult(
                    launchResult.Fail(
                        AndroidAppLaunchStage.PackageUnhidden,
                        issue,
                        blockDetail ?? "launchIntent=null"),
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
                    if (!AndroidUsageStatsAccessApi.HasAccess(this, LogTag, false))
                        startedResult = startedResult.WithIssue(
                            AndroidAppLaunchIssueKind.UsageAccessDenied,
                            "usageStatsAccess=denied");

                    _launchResult = startedResult;
                    Log.Debug(
                        LogTag,
                        $"StartActivity returned for {request.PackageName}. component={launchIntent.Component?.FlattenToShortString() ?? "<none>"}, flags={launchIntent.Flags}, proxyTaskId={TaskId}.");
                    Log.Debug(LogTag, $"Starting hidden-session monitor for {request.PackageName}, taskId={TaskId}.");
                    HiddenAppSessionMonitorService.StartMonitoring(
                        this,
                        request.PackageName,
                        request.DisplayName,
                        TaskId,
                        startedResult,
                        AndroidIntentExtras.ReadParentFrozenCallback(Intent));
                    Log.Debug(LogTag, $"Monitor service request sent for {request.PackageName}.");
                    FinishWithLaunchResult(startedResult, false);
                }
                catch (ActivityNotFoundException exception)
                {
                    var failedResult = resultToStart.Fail(
                        AndroidAppLaunchStage.StartActivityFailedWithException,
                        AndroidAppLaunchIssueKind.MissingLauncherActivity,
                        exception.ToString());
                    failedResult = TryHideImmediately(request, "activity_not_found", failedResult);
                    FinishWithLaunchResult(failedResult, true);
                }
                catch (Exception exception)
                {
                    Log.Error(LogTag, $"Failed to launch {request.PackageName}: {exception}");
                    var failedResult = resultToStart.Fail(
                        AndroidAppLaunchStage.StartActivityFailedWithException,
                        AndroidAppLaunchResult.ClassifyStartActivityException(exception),
                        exception.ToString());
                    failedResult = TryHideImmediately(request, "launch_failed", failedResult);
                    FinishWithLaunchResult(failedResult, true);
                }
            });
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Proxy flow failed for {request.PackageName}: {exception}");
            FinishWithLaunchResult(
                GetLaunchResult(request).Fail(
                    AndroidAppLaunchStage.CommandReceived,
                    AndroidAppLaunchResult.ClassifyStartActivityException(exception),
                    exception.ToString(),
                    $"Android не смог подготовить {request.DisplayName} к запуску."),
                true);
        }
    }

    private async Task PrepareVpnIfNeededAndForwardAsync(HiddenAppLaunchRequest request)
    {
        try
        {
            if (IsSystemWorkProfileRequest(request))
            {
                ServiceRegistry.GetRequiredService<LocalStorageManager>().SetBoolean(StorageKeys.HaveActiveVpnSession, false);
                Log.Debug(LogTag, $"Shortcut launch: skipping VPN Guard for system work-profile app {request.PackageName}.");
                ForwardLaunchToManagedProfile(request, isSystem: true);
                return;
            }

            if (!ServiceRegistry.GetRequiredService<LocalStorageManager>().GetBoolean(StorageKeys.DisableVpnBeforeWorkLaunch))
            {
                ServiceRegistry.GetRequiredService<LocalStorageManager>().SetBoolean(StorageKeys.HaveActiveVpnSession, false);
                Log.Debug(LogTag, "Disable-VPN-before-shortcut-launch is disabled in settings.");
                ForwardLaunchToManagedProfile(request);
                return;
            }

            Log.Info(LogTag, $"VPN Guard is enabled for shortcut launch. package={request.PackageName}.");
            var prepareIntent = VpnService.Prepare(this);
            ServiceRegistry.GetRequiredService<LocalStorageManager>().SetBoolean(StorageKeys.HaveActiveVpnSession, false);
            if (prepareIntent is not null)
            {
                Log.Info(LogTag, "Shortcut launch: Android confirmation is required for VPN control.");
                _pendingVpnDisconnectRequest = request;
                RunOnUiThread(() => StartActivityForResult(prepareIntent, PrepareVpnRequestCode));
                return;
            }

            if (!AndroidVpnApi.IsVpnActive(this))
            {
                ServiceRegistry.GetRequiredService<LocalStorageManager>().SetBoolean(StorageKeys.HaveActiveVpnSession, false);
                Log.Info(LogTag, "Shortcut launch: no active VPN detected.");
                ForwardLaunchToManagedProfile(request);
                return;
            }

            await DisconnectVpnAndForwardAsync(request).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Failed to prepare VPN disconnect for shortcut launch: {exception}");
            ShowErrorAndFinish("Agnosia не смог отключить VPN перед запуском ярлыка.");
        }
    }

    private async Task DisconnectVpnAndForwardAsync(HiddenAppLaunchRequest request)
    {
        var result = await TransientVpnDisconnectService.DisconnectPreparedVpnAsync(this).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            ShowErrorAndFinish(result.Message);
            return;
        }

        if (AndroidVpnApi.IsVpnActive(this))
        {
            ServiceRegistry.GetRequiredService<LocalStorageManager>().SetBoolean(StorageKeys.HaveActiveVpnSession, false);
            ShowErrorAndFinish("VPN все еще активен в личном профиле. Сторонний клиент мог сразу подключиться снова.");
            return;
        }

        ServiceRegistry.GetRequiredService<LocalStorageManager>().SetBoolean(StorageKeys.HaveActiveVpnSession, true);
        OverlayVpnService.ShowOverlay(this);
        _pendingVpnDisconnectRequest = null;
        ForwardLaunchToManagedProfile(request);
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

    private void ForwardLaunchToManagedProfile(HiddenAppLaunchRequest request, bool isSystem = false)
    {
        RunOnUiThread(() =>
        {
            try
            {
                var proxyIntent = HiddenAppShortcutManager.CreateInternalLaunchIntent(request.PackageName,
                    request.TargetActivity,
                    request.DisplayName);
                var isSystemLaunch = isSystem || request.IsSystem;
                proxyIntent.PutExtra(AndroidCommandContract.ExtraIsSystem, isSystemLaunch);
                if (!isSystemLaunch)
                    proxyIntent.PutExtra(
                        AndroidCommandContract.ExtraParentFrozenCallback,
                        AgnosiaPendingIntentFactory.CreateWorkAppFrozenBroadcastPendingIntent(
                            this,
                            typeof(WorkAppFrozenReceiver),
                            request.PackageName));
                proxyIntent.AddFlags(ActivityFlags.NewTask);
                if (AgnosiaUtilities.TryTransferToProfileAndStartActivity(
                        this,
                        proxyIntent,
                        LogTag,
                        $"Android не смог открыть {request.DisplayName} в рабочем профиле.",
                        out var error))
                {
                    Finish();
                    return;
                }

                ShowErrorAndFinish(error ?? $"Android не смог открыть {request.DisplayName} в рабочем профиле.");
            }
            catch (Exception exception)
            {
                Log.Error(LogTag,
                    $"Failed to forward launch of {request.PackageName} to the work profile: {exception}");
                ShowErrorAndFinish($"Android не смог открыть {request.DisplayName} в рабочем профиле.");
            }
        });
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

    private void RehideAndFinish(HiddenAppLaunchRequest request)
    {
        if (_rehideStarted) return;

        _rehideStarted = true;

        try
        {
            Log.Info(
                LogTag,
                $"Launch flow finished for {request.PackageName}. Waiting for the session monitor to re-hide it after the app is minimized or closed.");
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to finalize proxy flow for {request.PackageName}: {exception}");
        }

        Finish();
    }

    private AndroidAppLaunchResult TryHideImmediately(
        HiddenAppLaunchRequest request,
        string reason,
        AndroidAppLaunchResult launchResult)
    {
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
                launchResult = launchResult.WithStage(
                    AndroidAppLaunchStage.PackageRehidden,
                    $"proxy_fallback:{reason}");
                launchResult.Log(LogTag);
                var result = AndroidProfileCommandGateway.NotifyParentWorkAppFrozen(
                    this,
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
