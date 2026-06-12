using Agnosia.Android.Api.Notifications;
using Agnosia.Android.Api.Platform;
using Agnosia.Android.Platform;
using Agnosia.Android.Storage;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Vpn;

[Service(
    Name = "com.agnosia.app.LockdownVpnService",
    Permission = "android.permission.BIND_VPN_SERVICE",
    Exported = true,
    ForegroundServiceType = ForegroundService.TypeSystemExempted)]
[IntentFilter(["android.net.VpnService"])]
[MetaData("android.net.VpnService.SUPPORTS_ALWAYS_ON", Value = "true")]
public sealed class LockdownVpnService : VpnService
{
    private const string LogTag = "AgnosiaLockdownVpn";
    private const string ActionRefresh = "agnosia.action.REFRESH_LOCKDOWN_VPN";
    private const string ActionStop = "agnosia.action.STOP_LOCKDOWN_VPN";
    private const int NotificationId = 0x10CDA;
    private const string NotificationChannelId = "agnosia.lockdown-vpn";
    private const string NotificationChannelName = "Agnosia Lockdown";

    private const string NotificationChannelDescription =
        "Блокировка интернета выбранным приложениям рабочего профиля";

    private readonly object _sync = new();
    private ParcelFileDescriptor? _vpnInterface;

    public static void StartOrRefresh(Context context)
    {
        var intent = new Intent(context, typeof(LockdownVpnService));
        intent.SetAction(ActionRefresh);
        try
        {
            context.StartForegroundService(intent);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag, $"Failed to start Lockdown VPN service: {exception}");
        }
    }

    public static void Stop(Context context)
    {
        var intent = new Intent(context, typeof(LockdownVpnService));
        intent.SetAction(ActionStop);
        try
        {
            context.StartService(intent);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag, $"Failed to stop Lockdown VPN service: {exception}");
        }
    }

    public override void OnCreate()
    {
        base.OnCreate();
        AgnosiaRuntime.Initialize(this);
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (string.Equals(intent?.Action, ActionStop, StringComparison.Ordinal)
            || !LockdownSettingsStore.IsEnabled())
        {
            CloseVpnInterface();
            StopSelf(startId);
            return StartCommandResult.NotSticky;
        }

        try
        {
            StartForegroundServiceNotification();
            EstablishOrRefreshInterface();
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Failed to refresh Lockdown VPN service: {exception}");
            CloseVpnInterface();
        }

        return StartCommandResult.Sticky;
    }

    public override void OnRevoke()
    {
        base.OnRevoke();
        LockdownSettingsStore.SetEnabled(false);
        CloseVpnInterface();
        StopSelf();
    }

    public override void OnDestroy()
    {
        CloseVpnInterface();
        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent)
    {
        return null;
    }

    private void EstablishOrRefreshInterface()
    {
        var blockedPackages = LockdownSettingsStore.LoadBlockedPackages();
        var directNetworkPackages = LockdownVpnController.CreateDirectNetworkPackageAllowlist(this);
        var builder = new Builder(this)
            .SetSession("Agnosia Lockdown")
            .SetMtu(1280)
            .AddAddress("10.74.0.1", 32)
            .AddRoute("0.0.0.0", 0)
            .AddRoute("::", 0);

        foreach (var packageName in directNetworkPackages)
        {
            try
            {
                builder.AddDisallowedApplication(packageName);
            }
            catch (PackageManager.NameNotFoundException)
            {
                Log.Debug(LogTag, $"Skipping missing package in Lockdown VPN list. package={packageName}.");
            }
        }

        var nextInterface = builder.Establish();
        if (nextInterface is null)
        {
            Log.Warn(LogTag, "Android did not create Lockdown VPN interface.");
            return;
        }

        lock (_sync)
        {
            CloseVpnInterfaceCore();
            _vpnInterface = nextInterface;
        }

        Log.Info(
            LogTag,
            $"Lockdown VPN refreshed. blockedPackages={blockedPackages.Length}, directNetworkPackages={directNetworkPackages.Length}.");
    }

    private void CloseVpnInterface()
    {
        lock (_sync)
        {
            CloseVpnInterfaceCore();
        }
    }

    private void CloseVpnInterfaceCore()
    {
        try
        {
            _vpnInterface?.Close();
        }
        catch (IOException exception)
        {
            Log.Warn(LogTag, $"Failed to close Lockdown VPN interface: {exception.Message}");
        }

        _vpnInterface?.Dispose();
        _vpnInterface = null;
    }

    private void StartForegroundServiceNotification()
    {
        var smallIcon = ApplicationInfo?.Icon ?? global::Android.Resource.Drawable.SymDefAppIcon;
        var notification = AndroidNotificationApi.BuildNotification(
            this,
            NotificationChannelId,
            NotificationChannelName,
            NotificationChannelDescription,
            "Agnosia Lockdown активен",
            "Интернет выбранных приложений рабочего профиля заблокирован.",
            smallIcon);

        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeSystemExempted);
            return;
        }

        StartForeground(NotificationId, notification);
    }
}
