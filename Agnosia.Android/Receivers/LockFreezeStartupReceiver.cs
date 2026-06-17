using Agnosia.Android.Infrastructure;
using Agnosia.Android.Services;
using Android.Content;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Receivers;

[BroadcastReceiver(
    Name = "com.agnosia.app.LockFreezeStartupReceiver",
    Exported = true)]
[IntentFilter(
[
    Intent.ActionBootCompleted,
    Intent.ActionMyPackageReplaced,
    "android.intent.action.MANAGED_PROFILE_AVAILABLE",
    "android.intent.action.MANAGED_PROFILE_UNLOCKED"
])]
public sealed class LockFreezeStartupReceiver : BroadcastReceiver
{
    private const string LogTag = "AgnosiaLockFreezeStartup";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null) return;

        try
        {
            AgnosiaRuntime.Initialize(context);
            if (!AgnosiaUtilities.IsProfileOwner(context)) return;

            AndroidStartup.EnforceWorkProfilePolicies(context);
            var action = intent?.Action ?? "<unknown>";
            var result = LockFreezeCleanupJobService.Schedule(context, action);
            Log.Info(LogTag, $"Lock-freeze cleanup fallback after {action}: {result}.");
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to schedule lock-freeze cleanup fallback after system broadcast: {exception}");
        }
    }
}
