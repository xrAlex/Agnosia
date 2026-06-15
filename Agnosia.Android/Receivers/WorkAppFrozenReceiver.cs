using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Platform;
using Agnosia.Android.Vpn;
using Android.Content;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Receivers;

[BroadcastReceiver(
    Name = "com.agnosia.app.WorkAppFrozenReceiver",
    Exported = false)]
public sealed class WorkAppFrozenReceiver : BroadcastReceiver
{
    private const string LogTag = "AgnosiaWorkFrozenReceiver";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null) return;

        if (intent is null)
        {
            Log.Warn(LogTag, "Ignoring null work-app frozen broadcast.");
            return;
        }

        if (!string.Equals(intent.Action, AgnosiaActions.WorkAppFrozen, StringComparison.Ordinal))
        {
            Log.Warn(LogTag, $"Ignoring unexpected action={intent.Action ?? "<null>"}.");
            return;
        }

        if (!AuthenticationUtility.CheckWorkAppFrozenCallback(intent))
        {
            Log.Warn(LogTag, "Rejected work-app frozen broadcast: authentication check failed.");
            return;
        }

        var pendingResult = GoAsync();
        var appContext = context.ApplicationContext ?? context;
        var trigger = intent.GetStringExtra(AndroidProfileCommandGateway.ExtraTrigger) ?? "work_app_frozen_broadcast";
        _ = Task.Run(async () =>
        {
            try
            {
                Log.Info(LogTag, $"Work-app frozen broadcast received in parent profile. trigger={trigger}");

                var result = await WorkAppFrozenHandler.RestoreParentVpnAndHideOverlayAsync(
                    appContext,
                    trigger,
                    LogTag).ConfigureAwait(false);
                if (result.Succeeded)
                {
                    Log.Info(LogTag,
                        $"Work-app frozen broadcast handled successfully. trigger={trigger}, message={result.Message}");
                    return;
                }

                Log.Warn(LogTag,
                    $"Work-app frozen broadcast handling failed. trigger={trigger}, message={result.Message}");
            }
            catch (Exception exception)
            {
                Log.Error(LogTag, $"Failed to handle work-app frozen broadcast: {exception}");
            }
            finally
            {
                pendingResult?.Finish();
            }
        });
    }
}
