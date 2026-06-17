using Agnosia.Models;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Vpn;

[Service(
    Name = "com.agnosia.app.TransientVpnDisconnectService",
    Permission = "android.permission.BIND_VPN_SERVICE",
    Exported = true,
    ForegroundServiceType = ForegroundService.TypeSystemExempted)]
public sealed class TransientVpnDisconnectService : VpnService
{
    private const string LogTag = "AgnosiaTransientVpn";
    private const string ActionVpnService = "android.net.VpnService";
    private const string ActionDisconnect = "agnosia.action.DISCONNECT_ACTIVE_VPN";
    private const int NotificationId = 0x57C41;
    private const string NotificationChannelId = "agnosia.transient-vpn";
    private const string NotificationChannelName = "Agnosia VPN";

    private const string NotificationChannelDescription =
        "Временное отключение стороннего VPN перед запуском рабочего приложения";

    private const string StartServiceFailureMessage = "Agnosia не смог запустить временную VPN-службу.";

    private static readonly TimeSpan EstablishHoldTime = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan PostCloseDelay = TimeSpan.FromMilliseconds(120);
    private static readonly Lock Sync = new();

    private static TaskCompletionSource<OperationResult>? _pendingCompletion;

    private ParcelFileDescriptor? _vpnInterface;
    private bool _completed;

    public static Task<OperationResult> DisconnectPreparedVpnAsync(
        Context context,
        CancellationToken cancellationToken = default)
    {
        return Prepare(context) is not null
            ? Task.FromResult(OperationResult.Failure("Android еще не выдал Agnosia управление VPN."))
            : StartDisconnectServiceAsync(context, cancellationToken);
    }

    private static async Task<OperationResult> StartDisconnectServiceAsync(
        Context context,
        CancellationToken cancellationToken)
    {
        Task<OperationResult> completionTask;
        var shouldStartService = false;
        lock (Sync)
        {
            if (_pendingCompletion is not null && !_pendingCompletion.Task.IsCompleted)
            {
                completionTask = _pendingCompletion.Task;
            }
            else
            {
                _pendingCompletion =
                    new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                completionTask = _pendingCompletion.Task;
                shouldStartService = true;
            }
        }

        if (!shouldStartService) return await completionTask.ConfigureAwait(false);

        var intent = new Intent(context, typeof(TransientVpnDisconnectService));
        intent.SetAction(ActionDisconnect);

        try
        {
            context.StartForegroundService(intent);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            var failure = OperationResult.Failure(StartServiceFailureMessage);
            CompletePending(failure);
            Log.Error(LogTag, $"Failed to request transient VPN service start: {exception}");
            return failure;
        }

        await using var registration = cancellationToken.Register(static _ => CancelPending(), null);
        return await completionTask.ConfigureAwait(false);
    }

    public override void OnCreate()
    {
        base.OnCreate();
        AgnosiaRuntime.Initialize(this);
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (!string.Equals(intent?.Action, ActionDisconnect, StringComparison.Ordinal))
        {
            StopSelf(startId);
            return StartCommandResult.NotSticky;
        }

        try
        {
            StartForegroundServiceNotification();
            _ = Task.Run(() => RunDisconnectAsync(startId));
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Failed to start transient VPN service: {exception}");
            Complete(OperationResult.Failure(StartServiceFailureMessage));
            StopSelf(startId);
        }

        return StartCommandResult.NotSticky;
    }

    public override void OnRevoke()
    {
        base.OnRevoke();
        Complete(OperationResult.Failure("Android отозвал временное VPN-подключение Agnosia."));
        StopSelf();
    }

    public override void OnDestroy()
    {
        try
        {
            CloseVpnInterface();
        }
        catch (IOException exception)
        {
            Log.Warn(LogTag, $"Failed to close VPN interface during service shutdown: {exception.Message}");
        }

        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent)
    {
        return string.Equals(intent?.Action, ActionVpnService, StringComparison.Ordinal)
            ? base.OnBind(intent)
            : null;
    }

    private async Task RunDisconnectAsync(int startId)
    {
        try
        {
            Log.Info(LogTag, "Starting transient VPN to clear the active connection.");

            _vpnInterface = CreateEphemeralInterface();
            if (_vpnInterface is null)
            {
                Complete(OperationResult.Failure("Android не разрешил создать временный VPN-интерфейс."));
                return;
            }

            await Task.Delay(EstablishHoldTime).ConfigureAwait(false);

            CloseVpnInterface();

            await Task.Delay(PostCloseDelay).ConfigureAwait(false);
            Complete(OperationResult.Success("VPN отключен."));
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Failed to take over VPN temporarily: {exception}");
            Complete(OperationResult.Failure("Agnosia не смог автоматически отключить активный VPN."));
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

    private void CloseVpnInterface()
    {
        _vpnInterface?.Close();
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
            "Agnosia отключает VPN",
            "Временная VPN-служба остановится автоматически.",
            smallIcon);

        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeSystemExempted);
            return;
        }

        StartForeground(NotificationId, notification);
    }

    private void Complete(OperationResult result)
    {
        lock (Sync)
        {
            if (_completed) return;

            _completed = true;
            _pendingCompletion?.TrySetResult(result);
            _pendingCompletion = null;
        }
    }

    private static void CompletePending(OperationResult result)
    {
        lock (Sync)
        {
            _pendingCompletion?.TrySetResult(result);
            _pendingCompletion = null;
        }
    }

    private static void CancelPending()
    {
        lock (Sync)
        {
            _pendingCompletion?.TrySetCanceled();
            _pendingCompletion = null;
        }
    }
}
