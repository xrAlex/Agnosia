using Agnosia.Android.Commands;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Services;

public sealed partial class HiddenAppSessionMonitorService
{
    private void EnsureParentNotificationRetryLocked()
    {
        if (_storeState.PendingParentNotifications.Length == 0 || _parentNotificationRetryCts is not null) return;

        var cancellation = new CancellationTokenSource();
        _parentNotificationRetryCts = cancellation;
        _ = Task.Run(() => RetryParentNotificationsSafelyAsync(cancellation));
    }

    private async Task RetryParentNotificationsSafelyAsync(CancellationTokenSource cancellation)
    {
        var restartRequired = false;
        try
        {
            await RetryParentNotificationsAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Log.Error(LogTag, $"Parent notification retry loop failed: {exception}");
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellation.Token).ConfigureAwait(false);
                restartRequired = true;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_parentNotificationRetryCts, cancellation))
                {
                    _parentNotificationRetryCts = null;
                    if (restartRequired) EnsureParentNotificationRetryLocked();
                }
            }
        }
    }

    private async Task RetryParentNotificationsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HiddenAppPendingParentNotificationState[] due;
            TimeSpan delay;
            lock (_sync)
            {
                if (_storeState.PendingParentNotifications.Length == 0) return;

                var now = DateTimeOffset.UtcNow;
                due = _storeState.GetDueParentNotifications(now);
                delay = due.Length > 0
                    ? TimeSpan.Zero
                    : GetNextParentNotificationDelay(_storeState, now);
            }

            if (due.Length == 0)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            foreach (var pending in due)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await DeliverParentNotificationAsync(pending, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task DeliverParentNotificationAsync(
        HiddenAppPendingParentNotificationState pending,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (!_storeState.PendingParentNotifications.Any(item => string.Equals(
                    item.Session.SessionId,
                    pending.Session.SessionId,
                    StringComparison.Ordinal)))
            {
                return;
            }
        }

        var launchId = pending.Session.ParentCallbackLaunchId;
        AndroidCommandResultEnvelope result;
        if (string.IsNullOrWhiteSpace(launchId))
        {
            result = AndroidCommandResultEnvelope.Failure(
                Guid.Empty,
                AndroidCommandKind.WorkAppFrozen,
                AndroidCommandTransportKind.SilentParentProfile,
                "Parent callback launch identity is missing.",
                "work_app_frozen_launch_id_missing",
                TimeSpan.Zero,
                $"sessionId={pending.Session.SessionId}");
        }
        else
        {
            var trigger = $"session_hide:{pending.Reason}:{pending.Session.PackageName}";
            var envelope = new AndroidCommandEnvelope(
                Guid.NewGuid(),
                AndroidCommandKind.WorkAppFrozen,
                AndroidCommandTargetProfile.Personal,
                AndroidCommandInteractivity.Silent,
                AndroidCommandPriority.Mutation,
                TimeSpan.FromSeconds(30),
                WorkAppFrozenCommandPayload.Create(
                        pending.Session.PackageName,
                        launchId,
                        trigger)
                    .Serialize());
            result = await ServiceRegistry.GetRequiredService<AndroidCommandCenter>()
                .ExecuteAsync(envelope, cancellationToken)
                .ConfigureAwait(false);
        }

        HiddenAppSessionStoreState updatedState;
        lock (_sync)
        {
            if (!_storeState.PendingParentNotifications.Any(item => string.Equals(
                    item.Session.SessionId,
                    pending.Session.SessionId,
                    StringComparison.Ordinal)))
            {
                return;
            }

            updatedState = HiddenAppParentNotificationDelivery.ApplyResult(
                _storeState,
                pending.Session.SessionId,
                result.Succeeded,
                DateTimeOffset.UtcNow);
            _storeState = updatedState;
            PersistState(updatedState);
        }

        if (result.Succeeded)
        {
            Log.Info(
                LogTag,
                $"Parent profile confirmed work-app frozen callback. package={pending.Session.PackageName}, sessionId={pending.Session.SessionId}.");
        }
        else
        {
            Log.Warn(
                LogTag,
                $"Parent profile did not confirm work-app frozen callback. package={pending.Session.PackageName}, sessionId={pending.Session.SessionId}, error={result.ErrorCode ?? "failed"}.");
        }

        StopServiceIfIdleOrUpdateNotification(updatedState);
    }

    private static TimeSpan GetNextParentNotificationDelay(
        HiddenAppSessionStoreState state,
        DateTimeOffset now)
    {
        var nextAttemptAt = state.PendingParentNotifications.Min(
            pending => pending.NextAttemptAtUnixTimeMilliseconds);
        var delay = DateTimeOffset.FromUnixTimeMilliseconds(nextAttemptAt) - now;
        return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
    }

    private void CancelParentNotificationRetryLocked()
    {
        if (_parentNotificationRetryCts is null) return;

        _parentNotificationRetryCts.Cancel();
        _parentNotificationRetryCts.Dispose();
        _parentNotificationRetryCts = null;
    }
}
