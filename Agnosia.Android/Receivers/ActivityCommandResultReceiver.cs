using System.Collections.Concurrent;
using Agnosia.Android.Infrastructure;
using Android.Content;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Receivers;

// External UI may hold an Activity result until the user returns, past its HMAC lifetime.
// A one-shot callback delivers the same signed result immediately, even while paused.
[BroadcastReceiver(Name = "com.agnosia.app.ActivityCommandResultReceiver", Exported = false)]
public sealed class ActivityCommandResultReceiver : BroadcastReceiver
{
    private const string LogTag = "AgnosiaActivityCallback";
    private static readonly ConcurrentDictionary<Guid, PendingCommandResult> Pending = new();

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null) return;
        AgnosiaRuntime.Initialize(context);
        if (!Guid.TryParse(intent.GetStringExtra(AndroidCommandContract.ExtraCommandCorrelationId), out var id)
            || !Pending.TryGetValue(id, out var pending)) return;

        var resultCode = intent.GetIntExtra(AndroidCommandContract.ResultCommandResultCode, int.MinValue);
        var identity = ActivityCommandResultIdentity.Validate(id, pending.Kind, resultCode,
            intent.GetStringExtra(AndroidCommandContract.ExtraCommandCorrelationId),
            intent.GetStringExtra(AndroidCommandContract.ExtraCommandKind), resultCode);
        if (intent.Action != AgnosiaActions.CommandResult
            || (resultCode != (int)Result.Ok && resultCode != (int)Result.Canceled)
            || !identity.Succeeded || !AuthenticationUtility.CheckIntent(intent))
        {
            Log.Warn(LogTag, $"Rejected command result. correlationId={id}, kind={pending.Kind}.");
            return;
        }

        Log.Info(LogTag, $"Accepted command result. correlationId={id}, kind={pending.Kind}, result={resultCode}.");
        pending.Completion.TrySetResult(new AndroidActivityResult((Result)resultCode, new Intent(intent)));
    }

    internal static async Task<AndroidActivityResult> RunAsync(
        IAndroidActivityHost host, Intent intent, Guid correlationId, AndroidCommandKind kind,
        CancellationToken cancellationToken, TimeSpan? resultTimeout = null)
    {
        var completion = new TaskCompletionSource<AndroidActivityResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(resultTimeout ?? TimeSpan.FromSeconds(30));
        using var callbackIntent = new Intent(host.CurrentActivity, typeof(ActivityCommandResultReceiver));
        callbackIntent.SetAction(AgnosiaActions.CommandResult);
        callbackIntent.SetData(global::Android.Net.Uri.Parse($"agnosia://activity-result/{correlationId:D}"));
        using var callback = PendingIntent.GetBroadcast(host.CurrentActivity, 0, callbackIntent,
            PendingIntentFlags.OneShot | PendingIntentFlags.CancelCurrent | PendingIntentFlags.Mutable)
            ?? throw new InvalidOperationException("Cannot create command result callback.");
        if (!Pending.TryAdd(correlationId, new PendingCommandResult(kind, completion)))
            throw new InvalidOperationException("Activity command is already pending.");
        try
        {
            intent.PutExtra(AndroidCommandContract.ExtraCommandResultCallback, callback);
            var activityResult = host.StartForResultAsync(intent, timeout.Token,
                () => AuthenticationUtility.SignIntent(intent));
            // Either channel may be delayed or lost. Only a signed result with
            // the expected command identity is allowed to complete the operation.
            return await AuthenticatedActivityResultWaiter.WaitAsync(activityResult, completion.Task,
                result => result.IsLocalFailure || IsAuthenticatedActivityResult(result, correlationId, kind), timeout.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            Pending.TryRemove(correlationId, out _);
            timeout.Cancel();
            callback.Cancel();
        }
    }

    private static bool IsAuthenticatedActivityResult(
        AndroidActivityResult result, Guid correlationId, AndroidCommandKind kind)
    {
        var data = result.Data;
        return data?.Action == AgnosiaActions.CommandResult
               && (result.ResultCode == Result.Ok || result.ResultCode == Result.Canceled)
               && AuthenticationUtility.CheckIntent(data)
               && ActivityCommandResultIdentity.Validate(correlationId, kind, (int)result.ResultCode,
                   data.GetStringExtra(AndroidCommandContract.ExtraCommandCorrelationId),
                   data.GetStringExtra(AndroidCommandContract.ExtraCommandKind),
                   data.GetIntExtra(AndroidCommandContract.ResultCommandResultCode, int.MinValue)).Succeeded;
    }

    internal static void Send(Context context, Intent? request, Result resultCode, Intent result)
    {
        var callback = AndroidIntentExtras.ReadPendingIntent(request, AndroidCommandContract.ExtraCommandResultCallback);
        if (callback is null) return;
        try { callback.Send(context, resultCode, result); }
        catch (PendingIntent.CanceledException)
        {
            Log.Debug(LogTag, "Command result callback was canceled.");
        }
    }

    private sealed record PendingCommandResult(AndroidCommandKind Kind, TaskCompletionSource<AndroidActivityResult> Completion);
}
