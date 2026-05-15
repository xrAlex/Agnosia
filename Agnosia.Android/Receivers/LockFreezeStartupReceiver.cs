using Agnosia.Android.Api.Platform;
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

            AgnosiaUtilities.EnforceWorkProfilePolicies(
                context,
                typeof(AgnosiaDeviceAdminReceiver),
                MainActivity.LauncherActivityName);
            AgnosiaUtilities.EnforceUserRestrictions(context, typeof(AgnosiaDeviceAdminReceiver));
            Log.Info(LogTag, $"Starting lock-freeze monitor after {intent?.Action ?? "<unknown>"}.");
            WorkProfileLockFreezeService.EnsureRunning(context);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to start lock-freeze monitor after system broadcast: {exception}");
        }
    }
}
