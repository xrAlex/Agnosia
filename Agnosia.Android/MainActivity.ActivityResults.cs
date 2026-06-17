using Android.Content;
using Android.OS;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android;

public partial class MainActivity
{
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        TaskCompletionSource<AndroidActivityResult>? completionSource;
        lock (RequestSync)
        {
            PendingResults.Remove(requestCode, out completionSource);
        }

        Log.Debug(
            LogTag,
            $"Activity result received. requestCode={requestCode}, result={resultCode}, matchedPending={completionSource is not null}, hasData={data is not null}.");
        completionSource?.TrySetResult(new AndroidActivityResult(resultCode, data));
    }

    private Task<AndroidActivityResult> StartForResultAsync(
        Intent intent,
        CancellationToken cancellationToken = default)
    {
        var completionSource = new TaskCompletionSource<AndroidActivityResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration cancellationRegistration = default;

        int requestCode;
        lock (RequestSync)
        {
            requestCode = _nextRequestCode++;
            PendingResults[requestCode] = completionSource;
        }

        Log.Debug(
            LogTag,
            $"Activity result request registered. requestCode={requestCode}, action={intent.Action ?? "<none>"}, isResumed={_isResumed}.");

        if (cancellationToken.CanBeCanceled)
            cancellationRegistration = cancellationToken.Register(() =>
            {
                lock (RequestSync)
                {
                    PendingResults.Remove(requestCode);
                }

                Log.Debug(
                    LogTag,
                    $"Activity result request canceled. requestCode={requestCode}, action={intent.Action ?? "<none>"}.");
                completionSource.TrySetCanceled(cancellationToken);
            });

        _ = completionSource.Task.ContinueWith(
            static (_, state) => ((CancellationTokenRegistration)state!).Dispose(),
            cancellationRegistration,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        StartActivityForResultOnUiThread(intent, requestCode, completionSource);

        return completionSource.Task;
    }

    private void StartActivityForResultOnUiThread(
        Intent intent,
        int requestCode,
        TaskCompletionSource<AndroidActivityResult> completionSource)
    {
        var request = new ActivityStartRequest(intent, requestCode, completionSource);

        if (Looper.MainLooper?.IsCurrentThread == true)
        {
            ScheduleStart();
            return;
        }

        RunOnUiThread(ScheduleStart);
        return;

        void ScheduleStart()
        {
            if (!_isResumed)
            {
                QueueActivityStart(request);
                return;
            }

            StartActivityForResultRequest(request);
        }
    }

    private void DrainPendingActivityStarts()
    {
        _pendingDrainScheduled = false;
        var drained = 0;
        while (_isResumed && drained < MaxActivityStartsPerDrain)
        {
            ActivityStartRequest request;
            lock (RequestSync)
            {
                if (PendingActivityStarts.Count == 0) return;

                Log.Debug(LogTag, $"Draining queued activity start. remainingBefore={PendingActivityStarts.Count}.");
                request = PendingActivityStarts.Dequeue();
            }

            StartActivityForResultRequest(request);
            drained++;
        }

        if (_isResumed && HasPendingActivityStarts()) SchedulePendingActivityDrain();
    }

    private void QueueActivityStart(ActivityStartRequest request)
    {
        lock (RequestSync)
        {
            if (!PendingResults.ContainsKey(request.RequestCode)) return;

            if (IsIconCommand(request.Intent)) DropQueuedIconRequestsLocked("coalesced_icon_request");

            while (PendingActivityStarts.Count >= MaxPendingActivityStarts)
                DropOldestPendingActivityStartLocked("pending_start_queue_limit");

            Log.Debug(
                LogTag,
                $"Queueing activity start until resume. requestCode={request.RequestCode}, action={request.Intent.Action ?? "<none>"}, pendingStarts={PendingActivityStarts.Count + 1}.");
            PendingActivityStarts.Enqueue(request);
        }

        if (_isResumed) SchedulePendingActivityDrain();
    }

    private void StartActivityForResultRequest(ActivityStartRequest request)
    {
        lock (RequestSync)
        {
            if (!PendingResults.ContainsKey(request.RequestCode)) return;
        }

        try
        {
            Log.Debug(
                LogTag,
                $"Starting activity for result. requestCode={request.RequestCode}, action={request.Intent.Action ?? "<none>"}.");
            StartActivityForResult(request.Intent, request.RequestCode);
        }
        catch (Exception exception)
        {
            lock (RequestSync)
            {
                PendingResults.Remove(request.RequestCode);
            }

            Log.Warn(LogTag, $"Failed to start activity for result: {exception}");
            request.CompletionSource.TrySetResult(
                AndroidActivityResultApi.CreateCanceledResult("Android не смог открыть нужный экран или действие."));
        }
    }

    private static bool HasPendingActivityStarts()
    {
        lock (RequestSync)
        {
            return PendingActivityStarts.Count > 0;
        }
    }

    private void SchedulePendingActivityDrain()
    {
        if (_pendingDrainScheduled) return;

        _pendingDrainScheduled = true;
        var handler = new Handler(Looper.MainLooper ??
                                  throw new InvalidOperationException("Android main looper is unavailable."));
        handler.PostDelayed(DrainPendingActivityStarts, PendingDrainDelayMilliseconds);
    }

    private static void DropQueuedIconRequestsLocked(string reason)
    {
        if (PendingActivityStarts.Count == 0) return;

        var retained = new Queue<ActivityStartRequest>();
        while (PendingActivityStarts.Count > 0)
        {
            var queued = PendingActivityStarts.Dequeue();
            if (IsIconCommand(queued.Intent))
            {
                CancelQueuedActivityStartLocked(queued, reason);
                continue;
            }

            retained.Enqueue(queued);
        }

        while (retained.Count > 0) PendingActivityStarts.Enqueue(retained.Dequeue());
    }

    private static void DropOldestPendingActivityStartLocked(string reason)
    {
        if (PendingActivityStarts.Count == 0) return;

        var request = PendingActivityStarts.Dequeue();
        CancelQueuedActivityStartLocked(request, reason);
    }

    private static void CancelQueuedActivityStartLocked(ActivityStartRequest request, string reason)
    {
        PendingResults.Remove(request.RequestCode);
        Log.Debug(
            LogTag,
            $"Dropping queued activity start. requestCode={request.RequestCode}, action={request.Intent.Action ?? "<none>"}, reason={reason}.");
        request.CompletionSource.TrySetResult(
            AndroidActivityResultApi.CreateCanceledResult("Android отменил устаревшую фоновую команду."));
    }

    private static bool IsIconCommand(Intent intent)
    {
        return string.Equals(intent.Action, AgnosiaActions.QueryAppIcon, StringComparison.Ordinal)
               || string.Equals(intent.Action, AgnosiaActions.QueryAppIcons, StringComparison.Ordinal);
    }

    private sealed record ActivityStartRequest(
        Intent Intent,
        int RequestCode,
        TaskCompletionSource<AndroidActivityResult> CompletionSource);
}
