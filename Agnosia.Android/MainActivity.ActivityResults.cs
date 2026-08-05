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
        TaskCompletionSource<AndroidActivityResult>? crossProfileCompletionSource = null;
        lock (RequestSync)
        {
            PendingResults.Remove(requestCode, out completionSource);
            if (completionSource is null && _pendingCrossProfileResult is not null)
            {
                crossProfileCompletionSource = _pendingCrossProfileResult;
                _pendingCrossProfileResult = null;
                _pendingCrossProfileStart = null;
            }
        }

        Log.Debug(
            LogTag,
            $"Activity result received. requestCode={requestCode}, result={resultCode}, matchedPending={completionSource is not null}, matchedCrossProfile={crossProfileCompletionSource is not null}, hasData={data is not null}.");
        completionSource?.TrySetResult(new AndroidActivityResult(resultCode, data));
        crossProfileCompletionSource?.TrySetResult(new AndroidActivityResult(resultCode, data));
    }

    private async Task<AndroidActivityResult> StartCrossProfileForResultAsync(
        Intent intent,
        UserHandle targetUser,
        CancellationToken cancellationToken)
    {
        await _crossProfileActivityGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var completionSource = new TaskCompletionSource<AndroidActivityResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            lock (RequestSync)
            {
                if (_pendingCrossProfileResult is not null)
                    throw new InvalidOperationException("A cross-profile activity result is already pending.");

                _pendingCrossProfileResult = completionSource;
                _pendingCrossProfileStart = new CrossProfileActivityStartRequest(
                    intent,
                    targetUser,
                    completionSource);
            }

            using var cancellationRegistration = cancellationToken.Register(
                () => CancelCrossProfileActivity(completionSource, cancellationToken));
            RunOnUiThread(StartPendingCrossProfileActivity);
            return await completionSource.Task.ConfigureAwait(false);
        }
        finally
        {
            lock (RequestSync)
            {
                if (ReferenceEquals(_pendingCrossProfileResult, completionSource))
                    _pendingCrossProfileResult = null;
                if (ReferenceEquals(_pendingCrossProfileStart?.CompletionSource, completionSource))
                    _pendingCrossProfileStart = null;
            }

            _crossProfileActivityGate.Release();
        }
    }

    private void StartPendingCrossProfileActivity()
    {
        CrossProfileActivityStartRequest? request;
        lock (RequestSync)
        {
            if (!_isResumed || _pendingCrossProfileStart is null) return;

            request = _pendingCrossProfileStart;
            _pendingCrossProfileStart = null;
        }

        try
        {
            var crossProfileApps = AndroidSystemApi.GetCrossProfileApps(this)
                                   ?? throw new InvalidOperationException(
                                       "Android cross-profile API is unavailable.");
            Log.Debug(
                LogTag,
                $"Starting explicit cross-profile activity. action={request.Intent.Action ?? "<none>"}, target={request.TargetUser}.");
            crossProfileApps.StartActivity(request.Intent, request.TargetUser, this);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag, $"Failed to start explicit cross-profile activity: {exception}");
            CompleteCrossProfileActivity(
                request.CompletionSource,
                AndroidActivityResultApi.CreateCanceledResult(
                    "Android не смог открыть действие Agnosia в рабочем профиле."));
        }
    }

    private void CancelPendingCrossProfileActivity(string message)
    {
        TaskCompletionSource<AndroidActivityResult>? completionSource;
        lock (RequestSync)
        {
            completionSource = _pendingCrossProfileResult;
            _pendingCrossProfileResult = null;
            _pendingCrossProfileStart = null;
        }

        completionSource?.TrySetResult(AndroidActivityResultApi.CreateCanceledResult(message));
    }

    private void CancelCrossProfileActivity(
        TaskCompletionSource<AndroidActivityResult> completionSource,
        CancellationToken cancellationToken)
    {
        lock (RequestSync)
        {
            if (ReferenceEquals(_pendingCrossProfileResult, completionSource))
                _pendingCrossProfileResult = null;
            if (ReferenceEquals(_pendingCrossProfileStart?.CompletionSource, completionSource))
                _pendingCrossProfileStart = null;
        }

        completionSource.TrySetCanceled(cancellationToken);
    }

    private void CompleteCrossProfileActivity(
        TaskCompletionSource<AndroidActivityResult> completionSource,
        AndroidActivityResult result)
    {
        lock (RequestSync)
        {
            if (ReferenceEquals(_pendingCrossProfileResult, completionSource))
                _pendingCrossProfileResult = null;
        }

        completionSource.TrySetResult(result);
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

    private sealed record CrossProfileActivityStartRequest(
        Intent Intent,
        UserHandle TargetUser,
        TaskCompletionSource<AndroidActivityResult> CompletionSource);
}
