using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Agnosia.Models;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Vpn;

[Service(
    Name = "com.agnosia.app.LockdownVpnService",
    Permission = "android.permission.BIND_VPN_SERVICE",
    Exported = true,
    ForegroundServiceType = ForegroundService.TypeSystemExempted)]
[IntentFilter([ActionVpnService])]
[IntentFilter([ActionVpnManagerEvent], Categories = [CategoryEventAlwaysOnStateChanged])]
[MetaData("android.net.VpnService.SUPPORTS_ALWAYS_ON", Value = "true")]
public sealed class LockdownVpnService : VpnService
{
    private const string LogTag = "AgnosiaLockdownVpn";
    private const string TransientLogTag = "AgnosiaTransientVpn";
    private const string ActionVpnService = "android.net.VpnService";
    private const string ActionVpnManagerEvent = "android.net.action.VPN_MANAGER_EVENT";
    private const string CategoryEventAlwaysOnStateChanged = "android.net.category.EVENT_ALWAYS_ON_STATE_CHANGED";
    private const string ActionDisconnect = "agnosia.action.DISCONNECT_ACTIVE_VPN";
    private const string ActionRefresh = "agnosia.action.REFRESH_LOCKDOWN_VPN";
    private const string ActionStop = "agnosia.action.STOP_LOCKDOWN_VPN";
    private const int NotificationId = 0x10CDA;
    private const int TransientNotificationId = 0x57C41;
    private const string NotificationChannelId = "agnosia.lockdown-vpn";
    private const string NotificationChannelName = "Agnosia Lockdown";
    private const string TransientNotificationChannelId = "agnosia.transient-vpn";
    private const string TransientNotificationChannelName = "Agnosia VPN";

    private const string NotificationChannelDescription =
        "Блокировка интернета выбранным приложениям рабочего профиля";

    private const string TransientNotificationChannelDescription =
        "Временное отключение стороннего VPN перед запуском рабочего приложения";

    private const string StartServiceFailureMessage = "Agnosia не смог запустить временную VPN-службу.";

    private static readonly TimeSpan TransientEstablishHoldTime = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan TransientPostCloseDelay = TimeSpan.FromMilliseconds(120);
    private static readonly Lock TransientSync = new();
    private static TaskCompletionSource<OperationResult>? _transientPendingCompletion;

    private readonly Lock _sync = new();
    private ParcelFileDescriptor? _vpnInterface;
    private bool _transientDisconnectActive;
    private bool _transientDisconnectCompleted;

    public static Task<OperationResult> DisconnectPreparedVpnAsync(
        Context context,
        CancellationToken cancellationToken = default)
    {
        AgnosiaRuntime.Initialize(context);
        Intent? prepareIntent;
        try
        {
            prepareIntent = Prepare(context);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            SetVpnControlPrepared(false);
            Log.Warn(TransientLogTag, $"Failed to verify VPN control before transient disconnect: {exception}");
            return Task.FromResult(OperationResult.Failure("Android не смог проверить доступ Agnosia к VPN."));
        }

        if (prepareIntent is not null)
        {
            SetVpnControlPrepared(false);
            return Task.FromResult(OperationResult.Failure("Android еще не выдал Agnosia управление VPN."));
        }

        SetVpnControlPrepared(true);
        return StartTransientDisconnectServiceAsync(context, cancellationToken);
    }

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

    private static async Task<OperationResult> StartTransientDisconnectServiceAsync(
        Context context,
        CancellationToken cancellationToken)
    {
        Task<OperationResult> completionTask;
        var shouldStartService = false;
        lock (TransientSync)
        {
            if (_transientPendingCompletion is not null && !_transientPendingCompletion.Task.IsCompleted)
            {
                completionTask = _transientPendingCompletion.Task;
            }
            else
            {
                _transientPendingCompletion =
                    new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                completionTask = _transientPendingCompletion.Task;
                shouldStartService = true;
            }
        }

        if (!shouldStartService) return await completionTask.ConfigureAwait(false);

        var intent = new Intent(context, typeof(LockdownVpnService));
        intent.SetAction(ActionDisconnect);

        try
        {
            context.StartForegroundService(intent);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            var failure = OperationResult.Failure(StartServiceFailureMessage);
            CompleteTransientPending(failure);
            Log.Error(TransientLogTag, $"Failed to request transient VPN service start: {exception}");
            return failure;
        }

        await using var registration = cancellationToken.Register(static _ => CancelTransientPending(), null);
        return await completionTask.ConfigureAwait(false);
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
        if (string.Equals(intent?.Action, ActionDisconnect, StringComparison.Ordinal))
        {
            try
            {
                _transientDisconnectActive = true;
                _transientDisconnectCompleted = false;
                StartTransientForegroundServiceNotification();
                _ = Task.Run(() => RunTransientDisconnectAsync(startId));
            }
            catch (Exception exception)
            {
                Log.Error(TransientLogTag, $"Failed to start transient VPN service: {exception}");
                CompleteTransient(OperationResult.Failure(StartServiceFailureMessage));
                StopSelf(startId);
            }

            return StartCommandResult.NotSticky;
        }

        _transientDisconnectActive = false;
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
        if (_transientDisconnectActive)
        {
            SetVpnControlPrepared(false);
            CompleteTransient(OperationResult.Failure("Android отозвал временное VPN-подключение Agnosia."));
            StopSelf();
            return;
        }

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
        return string.Equals(intent?.Action, ActionVpnService, StringComparison.Ordinal)
            ? base.OnBind(intent)
            : null;
    }

    private async Task RunTransientDisconnectAsync(int startId)
    {
        try
        {
            Log.Info(TransientLogTag, "Starting transient VPN to clear the active connection.");

            _vpnInterface = CreateEphemeralInterface();
            if (_vpnInterface is null)
            {
                CompleteTransient(OperationResult.Failure("Android не разрешил создать временный VPN-интерфейс."));
                return;
            }

            await Task.Delay(TransientEstablishHoldTime).ConfigureAwait(false);

            CloseVpnInterface();

            await Task.Delay(TransientPostCloseDelay).ConfigureAwait(false);
            CompleteTransient(OperationResult.Success("VPN отключен."));
        }
        catch (Exception exception)
        {
            Log.Error(TransientLogTag, $"Failed to take over VPN temporarily: {exception}");
            CompleteTransient(OperationResult.Failure("Agnosia не смог автоматически отключить активный VPN."));
        }
        finally
        {
            StopSelf(startId);
        }
    }

    private ParcelFileDescriptor? CreateEphemeralInterface()
    {
        var builder = new Builder(this)
            .SetSession("Agnosia VPN Override")
            .SetMtu(1280)
            .AddAddress("10.73.0.1", 32);

        return builder.Establish();
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

    private void StartTransientForegroundServiceNotification()
    {
        var smallIcon = ApplicationInfo?.Icon ?? global::Android.Resource.Drawable.SymDefAppIcon;
        var notification = AndroidNotificationApi.BuildNotification(
            this,
            TransientNotificationChannelId,
            TransientNotificationChannelName,
            TransientNotificationChannelDescription,
            "Agnosia отключает VPN",
            "Временная VPN-служба остановится автоматически.",
            smallIcon);

        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            StartForeground(TransientNotificationId, notification, ForegroundService.TypeSystemExempted);
            return;
        }

        StartForeground(TransientNotificationId, notification);
    }

    private void CompleteTransient(OperationResult result)
    {
        lock (TransientSync)
        {
            if (_transientDisconnectCompleted) return;

            _transientDisconnectCompleted = true;
            _transientPendingCompletion?.TrySetResult(result);
            _transientPendingCompletion = null;
        }
    }

    private static void CompleteTransientPending(OperationResult result)
    {
        lock (TransientSync)
        {
            _transientPendingCompletion?.TrySetResult(result);
            _transientPendingCompletion = null;
        }
    }

    private static void CancelTransientPending()
    {
        lock (TransientSync)
        {
            _transientPendingCompletion?.TrySetCanceled();
            _transientPendingCompletion = null;
        }
    }

    private static void SetVpnControlPrepared(bool prepared)
    {
        ServiceRegistry.GetRequiredService<LocalStorageManager>()
            .SetBoolean(StorageKeys.VpnControlPrepared, prepared);
    }
}
