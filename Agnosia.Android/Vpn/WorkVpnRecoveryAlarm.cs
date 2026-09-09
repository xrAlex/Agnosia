using Android.Content;
using Android.OS;
using Agnosia.Android.Services;
using Agnosia.Android.Receivers;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Vpn;

internal static class WorkVpnRecoveryAlarm
{
    internal const int DurableAcknowledgement = 73;
    internal const string ExtraAttempt = "agnosia.vpn.recovery_attempt";
    internal const string ExtraLaunchId = "agnosia.vpn.recovery_launch_id";
    internal const string ExtraPackageName = "agnosia.vpn.recovery_package";
    private const string Action = "agnosia.action.WORK_VPN_RECOVERY_ALARM";
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5)
    ];

    public static bool Schedule(
        Context context,
        Type receiverType,
        string packageName,
        string launchId,
        PendingIntent parentCallback,
        int attempt = 0)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(receiverType);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(launchId);
        ArgumentNullException.ThrowIfNull(parentCallback);

        if (context.GetSystemService(Context.AlarmService) is not AlarmManager alarms) return false;

        var intent = new Intent(context, receiverType);
        intent.SetAction(Action);
        intent.SetData(global::Android.Net.Uri.Parse(
            $"agnosia://work-vpn-recovery/{Uri.EscapeDataString(packageName)}/{Uri.EscapeDataString(launchId)}"));
        intent.PutExtra(ExtraPackageName, packageName);
        intent.PutExtra(ExtraLaunchId, launchId);
        intent.PutExtra(ExtraAttempt, attempt);
        intent.PutExtra(AndroidCommandContract.ExtraParentFrozenCallback, parentCallback);
        var alarm = PendingIntent.GetBroadcast(
            context,
            GetStableRequestCode(packageName, launchId),
            intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        if (alarm is null) return false;

        var delay = RetryDelays[Math.Clamp(attempt, 0, RetryDelays.Length - 1)];
        alarms.Set(
            AlarmType.ElapsedRealtimeWakeup,
            SystemClock.ElapsedRealtime() + (long)delay.TotalMilliseconds,
            alarm);
        return true;
    }

    private static int GetStableRequestCode(string packageName, string launchId)
    {
        unchecked
        {
            var hash = packageName.Aggregate(17, (current, symbol) => current * 31 + symbol);
            hash = launchId.Aggregate(hash, (current, symbol) => current * 31 + symbol);
            return hash & int.MaxValue;
        }
    }

    public static void Send(Context context, string packageName, string launchId, PendingIntent callback)
    {
        callback.Send(context, Result.Canceled, null,
            new Completion(packageName, launchId),
            null, null, AndroidPendingIntentApi.CreateSenderBackgroundActivityStartOptions());
    }

    private sealed class Completion(string packageName, string launchId)
        : Java.Lang.Object, PendingIntent.IOnFinished
    {
        public void OnSendFinished(PendingIntent? pendingIntent, Intent? intent, Result resultCode,
            string? resultData, Bundle? resultExtras)
        {
            if ((int)resultCode != DurableAcknowledgement) return;
            _ = Task.Run(() =>
            {
                try
                {
                    // Only the authenticated receiver's durable acceptance acknowledges delivery.
                    // The next alarm observes the removed outbox and releases its nested token.
                    HiddenAppSessionMonitorService.ConfirmParentNotification(packageName, launchId);
                }
                catch (Exception exception)
                {
                    Log.Warn("AgnosiaWorkVpnRecovery", $"Could not persist callback acknowledgement: {exception}");
                }
            });
        }
    }
}
