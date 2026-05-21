using Agnosia.Android.Api.Platform;
using Agnosia.Android.Api.Vpn;
using Android.Content;
using Android.OS;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

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

    public override IBinder? OnBind(Intent? intent)
    {
        return null;
    }

    private void RegisterScreenReceiver()
    {
        if (_receiverRegistered) return;

        RegisterReceiver(_screenStateReceiver, new IntentFilter(Intent.ActionScreenOff));
        _receiverRegistered = true;
    }

    private void FreezeIfDeviceIsAlreadyNonInteractive(string trigger)
    {
        if (AndroidSystemApi.GetPowerManager(this)?.IsInteractive != false) return;

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
            if (context is null ||
                !string.Equals(intent?.Action, Intent.ActionScreenOff, StringComparison.Ordinal)) return;

            var pendingResult = GoAsync();
            var appContext = context.ApplicationContext ?? context;
            _ = Task.Run(() =>
            {
                try
                {
                    if (AgnosiaUtilities.IsProfileOwner(appContext))
                        FreezeWorkProfileApps(appContext, "screen_lock");
                    else
                        EnableParentVpnAfterScreenLock(appContext, "screen_lock");
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
        if (!AgnosiaUtilities.IsProfileOwner(context)) return;

        if (HiddenAppSessionMonitorService.CompletePersistedSessionForScreenLock(context))
        {
            Log.Info(LogTag, $"Screen lock freeze completed from active hidden session. trigger={trigger}.");
        }
    }

    private static void EnableParentVpnAfterScreenLockAsync(Context context, string trigger)
    {
        var appContext = context.ApplicationContext ?? context;
        _ = Task.Run(() => EnableParentVpnAfterScreenLock(appContext, trigger));
    }

    private static void EnableParentVpnAfterScreenLock(Context context, string trigger)
    {
        if (AgnosiaUtilities.IsProfileOwner(context)) return;

        var result = AndroidVpnAutomationApi
            .EnableConfiguredVpnAfterWorkFreezeAsync(context, $"parent_screen_lock:{trigger}")
            .GetAwaiter()
            .GetResult();
        if (!result.Succeeded) Log.Warn(LogTag, $"Parent VPN restore after screen lock failed: {result.Message}");
    }
}