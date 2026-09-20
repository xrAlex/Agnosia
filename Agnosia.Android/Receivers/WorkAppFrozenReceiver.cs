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
        var packageName = intent.GetStringExtra(AndroidCommandContract.ExtraCallbackPackage)!;
        var launchId = intent.GetStringExtra(AndroidCommandContract.ExtraCallbackLaunchId);
        var trigger = intent.GetStringExtra(AndroidProfileCommandGateway.ExtraTrigger) ?? "work_app_frozen_broadcast";
        var ordered = IsOrderedBroadcast;
        _ = Task.Run(async () =>
        {
            try
            {
                Log.Info(LogTag, $"Work-app frozen broadcast received in parent profile. trigger={trigger}");

                AgnosiaRuntime.Initialize(appContext);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(7));
                var completion = await ServiceRegistry.GetRequiredService<VpnRestoreOwnershipCoordinator>()
                    .AcceptCompletionAsync(packageName, launchId, timeout.Token).ConfigureAwait(false);
                if (completion.Result.Succeeded)
                {
                    AndroidQueryCache.Shared.ClearAppInventoryQueries();
                    ServiceRegistry.NotifyWorkAppFrozen(packageName);
                    if (completion.OwnerMatched && !VpnRestoreRetryScheduler.Schedule(
                            appContext, typeof(Activities.VpnRestoreRecoveryActivity), packageName, launchId))
                        return;
                    if (ordered && pendingResult is not null)
                        pendingResult.ResultCode = (Result)WorkVpnRecoveryAlarm.DurableAcknowledgement;
                    Log.Info(LogTag,
                        $"Work-app frozen broadcast accepted durably. trigger={trigger}");
                    return;
                }

                Log.Warn(LogTag,
                    $"Work-app frozen broadcast handling failed. trigger={trigger}, message={completion.Result.Message}");
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
