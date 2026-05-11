using Agnosia.Android.Api;
using Agnosia.Android.Receivers;
using Android.Content;
using Android.OS;
using Log = Agnosia.Android.Api.AgnosiaLog;

namespace Agnosia.Android.Services;

[Service(Exported = false)]
public sealed class WorkProfileLockFreezeService : Service
{
    private const string LogTag = "AgnosiaLockFreeze";
    private const string ActionStart = "agnosia.action.START_LOCK_FREEZE_MONITOR";

    private readonly ScreenStateReceiver _screenStateReceiver = new();
    private bool _receiverRegistered;

    public static void EnsureRunning(Context context)
    {
        var intent = new Intent(context, typeof(WorkProfileLockFreezeService));
        intent.SetAction(ActionStart);
        AndroidServiceApi.TryStartService(
            context,
            intent,
            LogTag,
            "Android не смог запустить монитор блокировки рабочего профиля.");
    }

    public override void OnCreate()
    {
        base.OnCreate();
        AgnosiaRuntime.Initialize(this);
        RegisterScreenReceiver();
        Log.Info(LogTag, "Work profile lock-freeze monitor started.");
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        RegisterScreenReceiver();
        FreezeIfDeviceIsAlreadyNonInteractive("service_started");
        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        if (_receiverRegistered)
        {
            UnregisterReceiver(_screenStateReceiver);
            _receiverRegistered = false;
        }

        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent) => null;

    private void RegisterScreenReceiver()
    {
        if (_receiverRegistered)
        {
            return;
        }

        RegisterReceiver(_screenStateReceiver, new IntentFilter(Intent.ActionScreenOff));
        _receiverRegistered = true;
    }

    private void FreezeIfDeviceIsAlreadyNonInteractive(string trigger)
    {
        if (AndroidSystemApi.GetPowerManager(this)?.IsInteractive != false)
        {
            return;
        }

        if (!AgnosiaUtilities.IsProfileOwner(this))
        {
            EnableParentVpnAfterScreenLockAsync(this, trigger);
            return;
        }

        Log.Info(LogTag, $"Device is already non-interactive; freezing work profile apps. trigger={trigger}");
        FreezeWorkProfileAppsAsync(this, trigger);
    }

    private static void FreezeWorkProfileAppsAsync(Context context, string trigger)
    {
        var appContext = context.ApplicationContext ?? context;
        _ = Task.Run(() => FreezeWorkProfileApps(appContext, trigger));
    }

    private sealed class ScreenStateReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (context is null || !string.Equals(intent?.Action, Intent.ActionScreenOff, StringComparison.Ordinal))
            {
                return;
            }

            var pendingResult = GoAsync();
            var appContext = context.ApplicationContext ?? context;
            _ = Task.Run(() =>
            {
                try
                {
                    if (AgnosiaUtilities.IsProfileOwner(appContext))
                    {
                        FreezeWorkProfileApps(appContext, "screen_lock");
                    }
                    else
                    {
                        EnableParentVpnAfterScreenLock(appContext, "screen_lock");
                    }
                }
                finally
                {
                    pendingResult?.Finish();
                }
            });
        }
    }

    private static void FreezeWorkProfileApps(Context context, string trigger)
    {
        if (!AgnosiaUtilities.IsProfileOwner(context))
        {
            return;
        }

        if (AndroidSystemApi.GetDevicePolicyManager(context) is not { } policyManager)
        {
            Log.Warn(LogTag, "DevicePolicyManager unavailable; could not freeze work profile apps on screen lock.");
            return;
        }

        if (context.PackageManager is not { } packageManager)
        {
            Log.Warn(LogTag, "PackageManager unavailable; could not enumerate work profile apps on screen lock.");
            return;
        }

        var admin = AgnosiaUtilities.GetAdminComponent(context, typeof(AgnosiaDeviceAdminReceiver));
        var frozenCount = 0;
        var failedCount = 0;
        var apps = packageManager.GetInstalledApplications(AndroidSystemApi.GetInstalledApplicationFlags());
        foreach (var app in apps)
        {
            var packageName = app.PackageName;
            if (!AndroidWorkProfilePackageClassifier.IsUserAppFreezeCandidate(
                    context,
                    packageManager,
                    policyManager,
                    app,
                    packageName))
            {
                continue;
            }

            try
            {
                var hiddenApplied = policyManager.SetApplicationHidden(admin, packageName, true);
                if (hiddenApplied || policyManager.IsApplicationHidden(admin, packageName))
                {
                    frozenCount++;
                }
                else
                {
                    failedCount++;
                    Log.Warn(LogTag, $"Android did not confirm hiding {packageName} on screen lock.");
                }
            }
            catch (Exception exception)
            {
                failedCount++;
                Log.Warn(LogTag, $"Failed to hide {packageName} on screen lock: {exception.Message}");
            }
        }

        HiddenAppSessionMonitorService.RequestScreenLockCompletion(context);
        Log.Info(LogTag, $"Screen lock freeze completed. trigger={trigger}, frozen={frozenCount}, failed={failedCount}.");
    }

    private static void EnableParentVpnAfterScreenLockAsync(Context context, string trigger)
    {
        var appContext = context.ApplicationContext ?? context;
        _ = Task.Run(() => EnableParentVpnAfterScreenLock(appContext, trigger));
    }

    private static void EnableParentVpnAfterScreenLock(Context context, string trigger)
    {
        if (AgnosiaUtilities.IsProfileOwner(context))
        {
            return;
        }

        var result = AndroidVpnAutomationApi
            .EnableConfiguredVpnAfterWorkFreezeAsync(context, $"parent_screen_lock:{trigger}")
            .GetAwaiter()
            .GetResult();
        if (!result.Succeeded)
        {
            Log.Warn(LogTag, $"Parent VPN restore after screen lock failed: {result.Message}");
        }
    }
}
