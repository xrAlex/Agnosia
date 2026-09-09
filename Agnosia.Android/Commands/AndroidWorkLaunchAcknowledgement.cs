using Agnosia.Models;
using Android.Content;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Commands;

internal static class AndroidWorkLaunchAcknowledgement
{
    public static void SendResult(Context context, Intent? request, OperationResult result)
    {
        var callback = Infrastructure.AndroidIntentExtras.ReadPendingIntent(request,
            AndroidCommandContract.ExtraLaunchAcknowledgement);
        if (callback is null) return;
        var data = new Intent();
        data.PutExtra(AndroidCommandContract.ResultLaunchAttemptSucceeded, result.Succeeded);
        data.PutExtra(AndroidCommandContract.ResultMessage, result.Message);
        try { callback.Send(context, 0, data); }
        catch (PendingIntent.CanceledException) { Log.Debug("AgnosiaLaunchAck", "Acknowledgement already sent or canceled."); }
    }

    public static async Task<OperationResult> SendAndWaitAsync(Context context, Intent intent,
        string packageName, string launchId, Func<CancellationToken, Task<OperationResult>> send,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var callback = AgnosiaPendingIntentFactory.CreateWorkLaunchAcknowledgement(context, packageName, launchId);
        var waiter = WorkLaunchAcknowledgements.Register(launchId, packageName);
        intent.PutExtra(AndroidCommandContract.ExtraLaunchAcknowledgement, callback);
        intent.PutExtra(AndroidCommandContract.ExtraCallbackLaunchId, launchId);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            return await waiter.DispatchAndWaitAsync(send, timeout.Token).ConfigureAwait(false);
        }
        finally
        {
            WorkLaunchAcknowledgements.Remove(launchId);
        }
    }

}
