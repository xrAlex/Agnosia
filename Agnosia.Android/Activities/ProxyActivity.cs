using Agnosia.Android.Api;
using Agnosia.Android.Receivers;
using Agnosia.Android.Services;
using Agnosia.Android.Shortcuts;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Java.Lang;
using Exception = System.Exception;
using Log = Agnosia.Android.Api.AgnosiaLog;

namespace Agnosia.Android.Activities;

[Activity(
    Name = "com.agnosia.app.ProxyActivity",
    Theme = "@android:style/Theme.Translucent.NoTitleBar",
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

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        AgnosiaRuntime.Initialize(this);
        TryStartProxyFlow();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent is null)
        {
            return;
        }

        Intent = intent;
        _launchStarted = false;
        _rehideStarted = false;
        _request = null;
        _pendingVpnDisconnectRequest = null;
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

        if (requestCode != LaunchRequestCode || _request is null)
        {
            return;
        }

        RehideAndFinish(_request);
    }

    private void TryStartProxyFlow()
    {
        if (_launchStarted)
        {
            return;
        }

        if (!HiddenAppShortcutManager.TryGetLaunchRequest(Intent, out var request))
        {
            Log.Warn(LogTag, $"Proxy launch request rejected. action={Intent?.Action ?? "<none>"}.");
            Finish();
            return;
        }

        Log.Info(
            LogTag,
            $"Proxy launch request accepted. package={request.PackageName}, targetActivity={request.TargetActivity ?? "<none>"}, displayName={request.DisplayName}.");
        _launchStarted = true;
        _request = request;

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
                await PrepareVpnIfNeededAndForwardAsync(request);
                return;
            }

            if (AndroidSystemApi.GetDevicePolicyManager(this) is not { } policyManager)
            {
                ShowErrorAndFinish("Android не предоставил сервис политики устройства.");
                return;
            }

            var admin = AgnosiaUtilities.GetAdminComponent(this, typeof(AgnosiaDeviceAdminReceiver));
            if (!AndroidPolicyApi.TrySetApplicationHidden(
                policyManager,
                admin,
                request.PackageName,
                hidden: false,
                LogTag,
                out var error))
            {
                ShowErrorAndFinish(error ?? $"Android не смог восстановить {request.DisplayName}.");
                return;
            }

            Intent? launchIntent = null;
            for (var attempt = 0; attempt < LaunchResolveAttempts; attempt++)
            {
                launchIntent = CreateLaunchIntent(request);
                if (launchIntent is not null)
                {
                    break;
                }

                await Task.Delay(LaunchResolveDelayMilliseconds);
            }

            if (launchIntent is null)
            {
                ShowErrorAndFinish($"Не найден способ открыть {request.DisplayName}.");
                return;
            }

            Log.Info(
                LogTag,
                $"Resolved launch intent for {request.PackageName}. component={launchIntent.Component?.FlattenToShortString() ?? "<none>"}, flags={launchIntent.Flags}.");

            RunOnUiThread(() =>
            {
                try
                {
                    Log.Info(LogTag, $"Starting hidden-session monitor for {request.PackageName}, taskId={TaskId}.");
                    HiddenAppSessionMonitorService.StartMonitoring(
                        this,
                        request.PackageName,
                        request.DisplayName,
                        TaskId,
                        ReadParentFrozenCallback(Intent));
                    Log.Info(LogTag, $"Monitor service request sent for {request.PackageName}.");
                    StartActivityForResult(launchIntent, LaunchRequestCode);
                }
                catch (ActivityNotFoundException)
                {
                    TryHideImmediately(request, "activity_not_found");
                    ShowErrorAndFinish($"Для {request.DisplayName} не найдена активность запуска.");
                }
                catch (Exception exception)
                {
                    Log.Error(LogTag, $"Failed to launch {request.PackageName}: {exception}");
                    TryHideImmediately(request, "launch_failed");
                    ShowErrorAndFinish($"Android не смог открыть {request.DisplayName}.");
                }
            });
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Proxy flow failed for {request.PackageName}: {exception}");
            ShowErrorAndFinish($"Android не смог подготовить {request.DisplayName} к запуску.");
        }
    }

    private async Task PrepareVpnIfNeededAndForwardAsync(HiddenAppLaunchRequest request)
    {
        try
        {
            if (!LocalStorageManager.Instance.GetBoolean(StorageKeys.DisableVpnBeforeWorkLaunch))
            {
                LocalStorageManager.Instance.SetBoolean(StorageKeys.HaveActiveVpnSession, false);
                Log.Info(LogTag, "Disable-VPN-before-shortcut-launch is disabled in settings.");
                ForwardLaunchToManagedProfile(request);
                return;
            }

            if (!AndroidVpnApi.IsVpnActive(this))
            {
                LocalStorageManager.Instance.SetBoolean(StorageKeys.HaveActiveVpnSession, false);
                Log.Info(LogTag, "Shortcut launch: no active VPN detected.");
                ForwardLaunchToManagedProfile(request);
                return;
            }

            var prepareIntent = VpnService.Prepare(this);
            LocalStorageManager.Instance.SetBoolean(StorageKeys.HaveActiveVpnSession, false);
            if (prepareIntent is not null)
            {
                Log.Info(LogTag, "Shortcut launch: Android confirmation is required for VPN control.");
                _pendingVpnDisconnectRequest = request;
                RunOnUiThread(() => StartActivityForResult(prepareIntent, PrepareVpnRequestCode));
                return;
            }

            await DisconnectVpnAndForwardAsync(request);
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Failed to prepare VPN disconnect for shortcut launch: {exception}");
            ShowErrorAndFinish("Agnosia не смог отключить VPN перед запуском ярлыка.");
        }
    }

    private async Task DisconnectVpnAndForwardAsync(HiddenAppLaunchRequest request)
    {
        var result = await TransientVpnDisconnectService.DisconnectPreparedVpnAsync(this);
        if (!result.Succeeded)
        {
            ShowErrorAndFinish(result.Message);
            return;
        }

        if (AndroidVpnApi.IsVpnActive(this))
        {
            LocalStorageManager.Instance.SetBoolean(StorageKeys.HaveActiveVpnSession, false);
            ShowErrorAndFinish("VPN все еще активен в личном профиле. Сторонний клиент мог сразу подключиться снова.");
            return;
        }

        LocalStorageManager.Instance.SetBoolean(StorageKeys.HaveActiveVpnSession, true);
        _pendingVpnDisconnectRequest = null;
        ForwardLaunchToManagedProfile(request);
    }

    private void RunInBackground(Func<Task> operation, string userFailureMessage)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await operation();
            }
            catch (Exception exception)
            {
                Log.Error(LogTag, $"Background proxy operation failed: {exception}");
                ShowErrorAndFinish(userFailureMessage);
            }
        });
    }

    private void ForwardLaunchToManagedProfile(HiddenAppLaunchRequest request)
    {
        RunOnUiThread(() =>
        {
            try
            {
                // Show overlay before launching work app so it's visible when VPN TempActivity starts after freeze.
                OverlayVpnService.ShowOverlay(this);

                var proxyIntent = HiddenAppShortcutManager.CreateInternalLaunchIntent(request.PackageName,
                    request.TargetActivity,
                    request.DisplayName);
                proxyIntent.PutExtra(
                    AndroidCommandContract.ExtraParentFrozenCallback,
                    AndroidPendingIntentApi.CreateWorkAppFrozenBroadcastPendingIntent(
                        this,
                        typeof(WorkAppFrozenReceiver),
                        request.PackageName));
                proxyIntent.AddFlags(ActivityFlags.NewTask);
                if (AndroidIntentApi.TryTransferToProfileAndStartActivity(
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
                Log.Error(LogTag, $"Failed to forward launch of {request.PackageName} to the work profile: {exception}");
                ShowErrorAndFinish($"Android не смог открыть {request.DisplayName} в рабочем профиле.");
            }
        });
    }

    private Intent? CreateLaunchIntent(HiddenAppLaunchRequest request)
    {
        var launchIntent = PackageManager?.GetLaunchIntentForPackage(request.PackageName);
        if (launchIntent is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.TargetActivity))
        {
            launchIntent.SetComponent(new ComponentName(request.PackageName, request.TargetActivity));
        }

        var flagsToClear = ActivityFlags.NewTask | ActivityFlags.ResetTaskIfNeeded;
        launchIntent.SetFlags(launchIntent.Flags & ~flagsToClear);
        launchIntent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        return launchIntent;
    }

    private void RehideAndFinish(HiddenAppLaunchRequest request)
    {
        if (_rehideStarted)
        {
            return;
        }

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

    private void TryHideImmediately(HiddenAppLaunchRequest request, string reason)
    {
        try
        {
            if (!AgnosiaUtilities.IsProfileOwner(this)
                || AndroidSystemApi.GetDevicePolicyManager(this) is not { } policyManager)
            {
                return;
            }

            var admin = AgnosiaUtilities.GetAdminComponent(this, typeof(AgnosiaDeviceAdminReceiver));
            if (AndroidPolicyApi.TrySetApplicationHidden(
                policyManager,
                admin,
                request.PackageName,
                hidden: true,
                LogTag,
                out _))
            {
                Log.Info(LogTag, $"App {request.PackageName} hidden again directly. reason={reason}");
                var result = AndroidProfileCommandGateway.NotifyParentWorkAppFrozen(
                    this,
                    $"proxy_fallback:{reason}:{request.PackageName}");
                if (!result.Succeeded)
                {
                    Log.Warn(LogTag, $"Could not notify parent profile about fallback freeze for {request.PackageName}: {result.Message}");
                }
            }
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Fallback re-hide for {request.PackageName} failed: {exception}");
        }
    }

    private static PendingIntent? ReadParentFrozenCallback(Intent? intent)
    {
        if (intent is null)
        {
            return null;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return intent.GetParcelableExtra(
                AndroidCommandContract.ExtraParentFrozenCallback,
                Class.FromType(typeof(PendingIntent))) as PendingIntent;
        }

#pragma warning disable CA1422
        return intent.GetParcelableExtra(AndroidCommandContract.ExtraParentFrozenCallback) as PendingIntent;
#pragma warning restore CA1422
    }

    private void ShowErrorAndFinish(string message)
    {
        Log.Warn(LogTag, $"Finishing proxy flow with error. package={_request?.PackageName ?? "<none>"}, message={message}");
        RunOnUiThread(() =>
        {
            Toast.MakeText(this, message, ToastLength.Long)?.Show();
            Finish();
        });
    }
}
