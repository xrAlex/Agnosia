using Android.Content;
using Android.OS;

namespace Agnosia.Android.Vpn;

internal static class VpnRestoreRetryScheduler
{
    internal const string ExtraAfterDeviceRestart = "agnosia.vpn.after_device_restart";
    internal const string ExtraAttempt = "agnosia.vpn.retry_attempt";
    internal const string ExtraLaunchId = "agnosia.vpn.retry_launch_id";
    internal const string ExtraPackageName = "agnosia.vpn.retry_package";
    private const string Action = "agnosia.action.RETRY_VPN_RESTORE";
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5)
    ];

    public static bool Schedule(
        Context context,
        Type activityType,
        string packageName,
        string? launchId,
        int attempt = 0,
        bool afterDeviceRestart = false)
    {
        if (context.GetSystemService(Context.AlarmService) is not AlarmManager alarms) return false;

        var intent = new Intent(context, activityType);
        intent.SetAction(Action);
        intent.SetData(CreateIdentity(packageName, launchId));
        intent.PutExtra(ExtraPackageName, packageName);
        if (!string.IsNullOrWhiteSpace(launchId)) intent.PutExtra(ExtraLaunchId, launchId);
        intent.PutExtra(ExtraAttempt, attempt);
        intent.PutExtra(ExtraAfterDeviceRestart, afterDeviceRestart);
        var pendingIntent = AndroidPendingIntentApi.CreateBackgroundActivityStartPendingIntent(
            context,
            intent,
            Action);
        var delay = RetryDelays[Math.Clamp(attempt, 0, RetryDelays.Length - 1)];
        alarms.Set(
            AlarmType.ElapsedRealtimeWakeup,
            SystemClock.ElapsedRealtime() + (long)delay.TotalMilliseconds,
            pendingIntent);
        return true;
    }

    public static void Cancel(Context context, string packageName, string? launchId)
    {
        if (context.GetSystemService(Context.AlarmService) is not AlarmManager alarms) return;
        var intent = new Intent(context, typeof(Activities.VpnRestoreRecoveryActivity));
        intent.SetAction(Action);
        intent.SetData(CreateIdentity(packageName, launchId));
        var pending = AndroidPendingIntentApi.CreateBackgroundActivityStartPendingIntent(context, intent, Action);
        alarms.Cancel(pending);
    }

    private static global::Android.Net.Uri? CreateIdentity(string packageName, string? launchId) =>
        global::Android.Net.Uri.Parse($"agnosia://vpn-restore/{Uri.EscapeDataString(packageName)}/{Uri.EscapeDataString(launchId ?? "legacy")}");
}
