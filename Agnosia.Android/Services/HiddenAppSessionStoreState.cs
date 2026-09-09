using Agnosia.Android.Api.Commands;

namespace Agnosia.Android.Services;

internal sealed partial record HiddenAppSessionState(
    string SessionId,
    string PackageName,
    string DisplayName,
    int TaskId,
    long StartedAtUnixTimeMilliseconds = 0,
    AndroidAppLaunchResult? LaunchResult = null)
{
    public string? ParentCallbackLaunchId { get; init; }
    public HiddenAppSessionState? PreviousSession { get; init; }

    public static HiddenAppSessionState Create(
        string packageName,
        string displayName,
        int taskId,
        long startedAtUnixTimeMilliseconds,
        AndroidAppLaunchResult? launchResult)
    {
        return new HiddenAppSessionState(
            Guid.NewGuid().ToString("N"),
            packageName,
            displayName,
            taskId,
            startedAtUnixTimeMilliseconds,
            launchResult);
    }

    public static HiddenAppSessionState Empty { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        -1);
}

internal sealed record HiddenAppPendingHideState(
    HiddenAppSessionState Session,
    string Reason,
    int FailedAttempts,
    long NextAttemptAtUnixTimeMilliseconds);

internal sealed record HiddenAppPendingParentNotificationState(
    HiddenAppSessionState Session,
    string Reason,
    int FailedAttempts,
    long NextAttemptAtUnixTimeMilliseconds);

internal sealed record HiddenAppSessionStoreState(
    HiddenAppSessionState? ActiveSession,
    HiddenAppPendingHideState[] PendingHides,
    HiddenAppPendingParentNotificationState[] PendingParentNotifications,
    int Version = HiddenAppSessionStoreState.CurrentVersion)
{
    public const int CurrentVersion = 3;
    public const string SessionReplacedReason = "session_replaced";
    public const string ScreenLockPersistedReason = "screen_lock_persisted_session";
    public const string TemporaryPolicyVisibilityReason = "temporary_policy_visibility";

    public static HiddenAppSessionStoreState Empty { get; } = new(null, [], []);

    public bool IsEmpty => ActiveSession is null
                           && PendingHides.Length == 0
                           && PendingParentNotifications.Length == 0;

    public bool RequiresPackageMonitoring => ActiveSession is not null || PendingHides.Length > 0;

    public HiddenAppSessionStoreState StartOrReplace(HiddenAppSessionState session, DateTimeOffset now)
    {
        var pendingHides = PendingHides
            .Where(pending => !string.Equals(
                pending.Session.PackageName,
                session.PackageName,
                StringComparison.Ordinal))
            .ToArray();
        var pendingParentNotifications = PendingParentNotifications
            .Where(pending => !string.Equals(
                pending.Session.PackageName,
                session.PackageName,
                StringComparison.Ordinal))
            .ToArray();

        if (ActiveSession is { } active
            && !string.Equals(active.PackageName, session.PackageName, StringComparison.Ordinal))
        {
            pendingHides = AddPending(pendingHides, active, SessionReplacedReason, now);
        }

        return this with
        {
            ActiveSession = session,
            PendingHides = pendingHides,
            PendingParentNotifications = pendingParentNotifications
        };
    }

    public HiddenAppSessionStoreState ReservePendingHide(
        HiddenAppSessionState session,
        string reason,
        DateTimeOffset now)
    {
        return this with { PendingHides = AddPending(PendingHides, session, reason, now) };
    }

    public HiddenAppSessionStoreState CancelActive(string sessionId)
    {
        return ActiveSession is { } active
               && string.Equals(active.SessionId, sessionId, StringComparison.Ordinal)
            ? RestorePreviousSession(active.PreviousSession)
            : this;
    }

    public HiddenAppSessionStoreState RestorePreviousSession(HiddenAppSessionState? previous)
    {
        return this with
        {
            ActiveSession = previous,
            PendingHides = PendingHides.Where(item => item.Session.SessionId != previous?.SessionId).ToArray()
        };
    }

    public HiddenAppSessionStoreState BeginCompletion(
        string sessionId,
        string reason,
        DateTimeOffset now)
    {
        if (ActiveSession is not { } active
            || !string.Equals(active.SessionId, sessionId, StringComparison.Ordinal))
        {
            return this;
        }

        return this with
        {
            ActiveSession = null,
            PendingHides = AddPending(PendingHides, active, reason, now)
        };
    }

    public HiddenAppSessionStoreState PrepareForScreenLock(DateTimeOffset now)
    {
        return ActiveSession is not { } active
            ? this
            : BeginCompletion(active.SessionId, ScreenLockPersistedReason, now);
    }

    public HiddenAppSessionStoreState RecordHideFailure(string sessionId, DateTimeOffset now)
    {
        var index = Array.FindIndex(
            PendingHides,
            pending => string.Equals(pending.Session.SessionId, sessionId, StringComparison.Ordinal));
        if (index < 0) return this;

        var failedAttempts = PendingHides[index].FailedAttempts + 1;
        var updated = PendingHides[index] with
        {
            FailedAttempts = failedAttempts,
            NextAttemptAtUnixTimeMilliseconds = now
                .Add(HiddenAppHideRetryPolicy.GetDelay(failedAttempts))
                .ToUnixTimeMilliseconds()
        };
        var pendingHides = PendingHides.ToArray();
        pendingHides[index] = updated;
        return this with { PendingHides = pendingHides };
    }

    public HiddenAppSessionStoreState ConfirmHidden(string sessionId, DateTimeOffset now)
    {
        var index = Array.FindIndex(
            PendingHides,
            pending => string.Equals(pending.Session.SessionId, sessionId, StringComparison.Ordinal));
        if (index < 0) return this;

        var completed = PendingHides[index];
        var pendingHides = PendingHides
            .Where((_, pendingIndex) => pendingIndex != index)
            .ToArray();
        if (string.Equals(completed.Reason, SessionReplacedReason, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(completed.Session.ParentCallbackLaunchId))
        {
            return this with { PendingHides = pendingHides };
        }

        return this with
        {
            PendingHides = pendingHides,
            PendingParentNotifications = AddPendingParentNotification(
                PendingParentNotifications,
                completed.Session,
                completed.Reason,
                now)
        };
    }

    public HiddenAppSessionStoreState RecordParentNotificationFailure(string sessionId, DateTimeOffset now)
    {
        var index = Array.FindIndex(
            PendingParentNotifications,
            pending => string.Equals(pending.Session.SessionId, sessionId, StringComparison.Ordinal));
        if (index < 0) return this;

        var failedAttempts = PendingParentNotifications[index].FailedAttempts + 1;
        var updated = PendingParentNotifications[index] with
        {
            FailedAttempts = failedAttempts,
            NextAttemptAtUnixTimeMilliseconds = now
                .Add(HiddenAppHideRetryPolicy.GetDelay(failedAttempts))
                .ToUnixTimeMilliseconds()
        };
        var notifications = PendingParentNotifications.ToArray();
        notifications[index] = updated;
        return this with { PendingParentNotifications = notifications };
    }

    public HiddenAppSessionStoreState ConfirmParentNotification(string packageName, string launchId)
    {
        var notifications = PendingParentNotifications
            .Where(pending => !(
                string.Equals(pending.Session.PackageName, packageName, StringComparison.Ordinal)
                && string.Equals(pending.Session.ParentCallbackLaunchId, launchId, StringComparison.Ordinal)))
            .ToArray();
        return notifications.Length == PendingParentNotifications.Length
            ? this
            : this with { PendingParentNotifications = notifications };
    }

    public string? GetPersistedLaunchId(string packageName)
    {
        if (ActiveSession is { } active
            && string.Equals(active.PackageName, packageName, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(active.ParentCallbackLaunchId))
        {
            return active.ParentCallbackLaunchId;
        }

        return PendingHides
                   .Where(pending => !string.Equals(
                       pending.Reason,
                       SessionReplacedReason,
                       StringComparison.Ordinal))
                   .Select(pending => pending.Session)
                   .Concat(PendingParentNotifications.Select(pending => pending.Session))
                   .Where(session => string.Equals(session.PackageName, packageName, StringComparison.Ordinal)
                                     && !string.IsNullOrWhiteSpace(session.ParentCallbackLaunchId))
                   .OrderByDescending(session => session.StartedAtUnixTimeMilliseconds)
                   .Select(session => session.ParentCallbackLaunchId)
                   .FirstOrDefault();
    }

    public HiddenAppPendingHideState[] GetDuePendingHides(DateTimeOffset now)
    {
        var nowUnixTimeMilliseconds = now.ToUnixTimeMilliseconds();
        return PendingHides
            .Where(pending => pending.NextAttemptAtUnixTimeMilliseconds <= nowUnixTimeMilliseconds)
            .ToArray();
    }

    public HiddenAppPendingParentNotificationState[] GetDueParentNotifications(DateTimeOffset now)
    {
        var nowUnixTimeMilliseconds = now.ToUnixTimeMilliseconds();
        return PendingParentNotifications
            .Where(pending => pending.NextAttemptAtUnixTimeMilliseconds <= nowUnixTimeMilliseconds)
            .ToArray();
    }

    private static HiddenAppPendingHideState[] AddPending(
        HiddenAppPendingHideState[] pendingHides,
        HiddenAppSessionState session,
        string reason,
        DateTimeOffset now)
    {
        if (pendingHides.Any(pending => string.Equals(
                pending.Session.SessionId,
                session.SessionId,
                StringComparison.Ordinal)))
        {
            return pendingHides;
        }

        return
        [
            ..pendingHides,
            new HiddenAppPendingHideState(
                session,
                reason,
                0,
                now.ToUnixTimeMilliseconds())
        ];
    }

    private static HiddenAppPendingParentNotificationState[] AddPendingParentNotification(
        HiddenAppPendingParentNotificationState[] notifications,
        HiddenAppSessionState session,
        string reason,
        DateTimeOffset now)
    {
        if (notifications.Any(pending => string.Equals(
                pending.Session.SessionId,
                session.SessionId,
                StringComparison.Ordinal)))
        {
            return notifications;
        }

        return
        [
            ..notifications,
            new HiddenAppPendingParentNotificationState(
                session,
                reason,
                0,
                now.ToUnixTimeMilliseconds())
        ];
    }

}

internal static class HiddenAppHideRetryPolicy
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(16),
        TimeSpan.FromSeconds(30)
    ];

    public static TimeSpan GetDelay(int failedAttempts)
    {
        var index = Math.Clamp(failedAttempts - 1, 0, Delays.Length - 1);
        return Delays[index];
    }
}

internal static class HiddenAppSessionConcurrency
{
    private static readonly SemaphoreSlim OperationGate = new(1, 1);

    public static async ValueTask<IDisposable> EnterOperationAsync(CancellationToken cancellationToken = default)
    {
        await OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new OperationLease(OperationGate);
    }

    public static IDisposable EnterOperation()
    {
        OperationGate.Wait();
        return new OperationLease(OperationGate);
    }

    public static bool TryEnterOperation(out IDisposable? lease)
    {
        if (!OperationGate.Wait(0))
        {
            lease = null;
            return false;
        }

        lease = new OperationLease(OperationGate);
        return true;
    }

    public static Task QueueMonitor(
        CancellationToken cancellationToken,
        Func<CancellationToken, Task> monitor,
        Func<Func<Task>, Task>? queue = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var capturedToken = cancellationToken;
        return queue is null
            ? Task.Run(() => monitor(capturedToken))
            : queue(() => monitor(capturedToken));
    }

    private sealed class OperationLease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;

        public void Dispose()
        {
            Interlocked.Exchange(ref _gate, null)?.Release();
        }
    }
}
