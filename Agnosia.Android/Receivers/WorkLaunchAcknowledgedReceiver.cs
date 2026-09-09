using Agnosia.Android.Commands;
using Android.Content;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Receivers;

[BroadcastReceiver(Name = "com.agnosia.app.WorkLaunchAcknowledgedReceiver", Exported = false)]
public sealed class WorkLaunchAcknowledgedReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        var launchId = intent?.GetStringExtra(AndroidCommandContract.ExtraCallbackLaunchId);
        var packageName = intent?.GetStringExtra(AndroidCommandContract.ExtraCallbackPackage);
        if (string.IsNullOrWhiteSpace(launchId) || string.IsNullOrWhiteSpace(packageName)
            || intent?.HasExtra(AndroidCommandContract.ResultLaunchAttemptSucceeded) != true) return;
        var acknowledgement = new WorkLaunchAcknowledgement(launchId, packageName,
            intent.GetBooleanExtra(AndroidCommandContract.ResultLaunchAttemptSucceeded, false),
            intent.GetStringExtra(AndroidCommandContract.ResultMessage) ?? string.Empty);
        var accepted = WorkLaunchAcknowledgements.Accept(acknowledgement);
        // An absent waiter is not a failed launch: persisted ownership already schedules reconciliation.
        Log.Info("AgnosiaLaunchAck", $"Launch attempt acknowledgement. launchId={launchId}, package={packageName}, succeeded={acknowledgement.Succeeded}, waiterAccepted={accepted}.");
    }
}
