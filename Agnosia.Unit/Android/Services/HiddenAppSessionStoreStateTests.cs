using Agnosia.Android.Api.Commands;
using Agnosia.Android.Services;
using Xunit;

namespace Agnosia.Unit.Android.Services;

public sealed class HiddenAppSessionStoreStateTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    [Fact]
    public void BeginCompletion_moves_active_session_to_pending()
    {
        var active = CreateSession("session-a", "com.example.a");
        var state = HiddenAppSessionStoreState.Empty.StartOrReplace(active, Now);

        var completed = state.BeginCompletion(active.SessionId, "task_removed", Now);

        Assert.Null(completed.ActiveSession);
        var pending = Assert.Single(completed.PendingHides);
        Assert.Equal(active.SessionId, pending.Session.SessionId);
        Assert.Equal("task_removed", pending.Reason);
        Assert.Equal(0, pending.FailedAttempts);
        Assert.Equal(Now.ToUnixTimeMilliseconds(), pending.NextAttemptAtUnixTimeMilliseconds);
    }

    [Fact]
    public void StartOrReplace_moves_different_package_to_pending_and_keeps_new_active()
    {
        var first = CreateSession("session-a", "com.example.a");
        var second = CreateSession("session-b", "com.example.b");

        var state = HiddenAppSessionStoreState.Empty
            .StartOrReplace(first, Now)
            .StartOrReplace(second, Now);

        Assert.Equal(second.SessionId, state.ActiveSession?.SessionId);
        var pending = Assert.Single(state.PendingHides);
        Assert.Equal(first.SessionId, pending.Session.SessionId);
        Assert.Equal(HiddenAppSessionStoreState.SessionReplacedReason, pending.Reason);
    }

    [Fact]
    public void StartOrReplace_same_package_does_not_schedule_rehide()
    {
        var first = CreateSession("session-a", "com.example.same");
        var second = CreateSession("session-b", "com.example.same");

        var state = HiddenAppSessionStoreState.Empty
            .StartOrReplace(first, Now)
            .StartOrReplace(second, Now);

        Assert.Equal(second.SessionId, state.ActiveSession?.SessionId);
        Assert.Empty(state.PendingHides);
        Assert.Same(state, state.BeginCompletion(first.SessionId, "stale", Now));
    }

    [Fact]
    public void StartOrReplace_same_package_cancels_older_pending_rehide()
    {
        var previous = CreateSession("session-old", "com.example.same");
        var next = CreateSession("session-new", "com.example.same");
        var state = HiddenAppSessionStoreState.Empty
            .StartOrReplace(previous, Now)
            .BeginCompletion(previous.SessionId, "task_removed", Now);

        var restarted = state.StartOrReplace(next, Now);

        Assert.Equal(next.SessionId, restarted.ActiveSession?.SessionId);
        Assert.Empty(restarted.PendingHides);
    }

    // Catches a failed launch clearing a newer reservation that arrived meanwhile.
    [Fact]
    public void CancelActive_removes_only_the_matching_launch_reservation()
    {
        var current = CreateSession("session-current", "com.example.current");
        var state = HiddenAppSessionStoreState.Empty.StartOrReplace(current, Now);

        var staleCancellation = state.CancelActive("session-stale");
        var matchingCancellation = state.CancelActive(current.SessionId);

        Assert.Same(state, staleCancellation);
        Assert.Null(matchingCancellation.ActiveSession);
    }

    // Catches losing the durable rollback obligation before a policy operation unhides a package.
    [Theory]
    [InlineData("com.example.same")]
    [InlineData("com.example.other")]
    public void Failed_launch_restores_previous_monitor_and_cancels_its_staged_hide(string nextPackage)
    {
        var previous = CreateSession("previous", "com.example.same");
        var reserved = CreateSession("reserved", nextPackage) with { PreviousSession = previous };
        var state = HiddenAppSessionStoreState.Empty.StartOrReplace(previous, Now).StartOrReplace(reserved, Now);
        Assert.True(HiddenAppSessionStoreCodec.TryDeserialize(HiddenAppSessionStoreCodec.Serialize(state), out var restored));
        var aborted = restored.CancelActive(reserved.SessionId);
        Assert.Equal(previous.SessionId, aborted.ActiveSession?.SessionId);
        Assert.Empty(aborted.PendingHides);
    }

    // Catches losing the durable rollback obligation before a policy operation unhides a package.
    [Fact]
    public void ReservePendingHide_preserves_active_session_and_records_cleanup_before_unhide()
    {
        var active = CreateSession("session-active", "com.example.active");
        var cleanup = CreateSession("cleanup-a", "com.example.policy");
        var state = HiddenAppSessionStoreState.Empty.StartOrReplace(active, Now);

        var reserved = state.ReservePendingHide(
            cleanup,
            HiddenAppSessionStoreState.TemporaryPolicyVisibilityReason,
            Now);

        Assert.Equal(active.SessionId, reserved.ActiveSession?.SessionId);
        var pending = Assert.Single(reserved.PendingHides);
        Assert.Equal(cleanup.SessionId, pending.Session.SessionId);
        Assert.Equal(HiddenAppSessionStoreState.TemporaryPolicyVisibilityReason, pending.Reason);
    }

    // Catches a stale callback obligation winning over a newer launch of the same package.
    [Fact]
    public void StartOrReplace_same_package_supersedes_older_parent_notification()
    {
        var previous = CreateSession("session-old", "com.example.same") with
        {
            ParentCallbackLaunchId = "launch-old"
        };
        var next = CreateSession("session-new", "com.example.same") with
        {
            ParentCallbackLaunchId = "launch-new"
        };
        var state = HiddenAppSessionStoreState.Empty
            .StartOrReplace(previous, Now)
            .BeginCompletion(previous.SessionId, "target_inactive", Now)
            .ConfirmHidden(previous.SessionId, Now);

        var restarted = state.StartOrReplace(next, Now.AddSeconds(1));

        Assert.Equal("launch-new", restarted.GetPersistedLaunchId("com.example.same"));
        Assert.Empty(restarted.PendingParentNotifications);
    }

    [Fact]
    public void RecordHideFailure_keeps_pending_session_and_schedules_next_attempt()
    {
        var session = CreateSession("session-a", "com.example.a");
        var state = HiddenAppSessionStoreState.Empty
            .StartOrReplace(session, Now)
            .BeginCompletion(session.SessionId, "task_removed", Now);

        var failed = state.RecordHideFailure(session.SessionId, Now);

        var pending = Assert.Single(failed.PendingHides);
        Assert.Equal(1, pending.FailedAttempts);
        Assert.Equal(Now.AddSeconds(1).ToUnixTimeMilliseconds(), pending.NextAttemptAtUnixTimeMilliseconds);
        Assert.Empty(failed.GetDuePendingHides(Now));
        Assert.Equal(session.SessionId, Assert.Single(failed.GetDuePendingHides(Now.AddSeconds(1))).Session.SessionId);
    }

    [Fact]
    public void ConfirmHidden_removes_only_matching_pending_identity()
    {
        var first = CreatePending("session-a", "com.example.a");
        var second = CreatePending("session-b", "com.example.b");
        var state = new HiddenAppSessionStoreState(null, [first, second], []);

        var confirmed = state.ConfirmHidden(first.Session.SessionId, Now);

        Assert.Equal(second.Session.SessionId, Assert.Single(confirmed.PendingHides).Session.SessionId);
    }

    [Fact]
    public void Confirming_old_identity_does_not_remove_new_same_package_session()
    {
        var active = CreateSession("session-new", "com.example.same");
        var stalePending = CreatePending("session-old", "com.example.same");
        var state = new HiddenAppSessionStoreState(active, [stalePending], []);

        var confirmed = state.ConfirmHidden(stalePending.Session.SessionId, Now);

        Assert.Equal(active.SessionId, confirmed.ActiveSession?.SessionId);
        Assert.Empty(confirmed.PendingHides);
    }

    // Catches dropping the cross-profile recovery identity before the parent accepted notification.
    [Fact]
    public void ConfirmHidden_creates_durable_parent_notification()
    {
        var session = CreateSession("session-a", "com.example.a") with
        {
            ParentCallbackLaunchId = "launch-a"
        };
        var state = HiddenAppSessionStoreState.Empty
            .StartOrReplace(session, Now)
            .BeginCompletion(session.SessionId, "target_inactive", Now);

        var confirmed = state.ConfirmHidden(session.SessionId, Now);

        Assert.Empty(confirmed.PendingHides);
        var notification = Assert.Single(confirmed.PendingParentNotifications);
        Assert.Equal("launch-a", notification.Session.ParentCallbackLaunchId);
        Assert.False(confirmed.IsEmpty);
    }

    // Ловит преждевременное восстановление VPN предыдущей сессии после передачи ownership новой.
    [Fact]
    public void ConfirmHidden_for_replaced_session_does_not_notify_parent()
    {
        var session = CreateSession("session-a", "com.example.a") with
        {
            ParentCallbackLaunchId = "launch-a"
        };
        var state = HiddenAppSessionStoreState.Empty
            .StartOrReplace(session, Now)
            .BeginCompletion(session.SessionId, HiddenAppSessionStoreState.SessionReplacedReason, Now);

        var confirmed = state.ConfirmHidden(session.SessionId, Now);

        Assert.True(confirmed.IsEmpty);
        Assert.Empty(confirmed.PendingHides);
        Assert.Empty(confirmed.PendingParentNotifications);
    }

    [Fact]
    public void GetPersistedLaunchId_prefers_active_over_older_pending_work()
    {
        var pending = CreateSession("session-pending", "com.example.same") with
        {
            ParentCallbackLaunchId = "launch-old"
        };
        var active = CreateSession("session-active", "com.example.same") with
        {
            ParentCallbackLaunchId = "launch-current"
        };
        var state = new HiddenAppSessionStoreState(
            active,
            [new HiddenAppPendingHideState(pending, "target_inactive", 0, Now.ToUnixTimeMilliseconds())],
            []);

        Assert.Equal("launch-current", state.GetPersistedLaunchId("com.example.same"));
    }

    [Fact]
    public void RecordParentNotificationFailure_keeps_identity_for_reconciliation()
    {
        var session = CreateSession("session-a", "com.example.a") with
        {
            ParentCallbackLaunchId = "launch-a"
        };
        var state = HiddenAppSessionStoreState.Empty
            .StartOrReplace(session, Now)
            .BeginCompletion(session.SessionId, "target_inactive", Now)
            .ConfirmHidden(session.SessionId, Now);

        var failed = state.RecordParentNotificationFailure(session.SessionId, Now);

        var pending = Assert.Single(failed.PendingParentNotifications);
        Assert.Equal(1, pending.FailedAttempts);
        Assert.Equal("launch-a", failed.GetPersistedLaunchId("com.example.a"));
    }

    // Catches an acknowledgement for one launch deleting another launch's durable recovery record.
    [Fact]
    public void ConfirmParentNotification_requires_matching_package_and_launch_identity()
    {
        var session = CreateSession("session-a", "com.example.a") with
        {
            ParentCallbackLaunchId = "launch-a"
        };
        var state = HiddenAppSessionStoreState.Empty
            .StartOrReplace(session, Now)
            .BeginCompletion(session.SessionId, "target_inactive", Now)
            .ConfirmHidden(session.SessionId, Now);

        var wrongPackage = state.ConfirmParentNotification("com.example.other", "launch-a");
        var wrongLaunch = state.ConfirmParentNotification("com.example.a", "launch-other");
        var confirmed = state.ConfirmParentNotification("com.example.a", "launch-a");

        Assert.Same(state, wrongPackage);
        Assert.Same(state, wrongLaunch);
        Assert.Empty(confirmed.PendingParentNotifications);
    }

    [Fact]
    public void PrepareForScreenLock_moves_active_to_pending_without_dropping_existing_pending()
    {
        var active = CreateSession("session-active", "com.example.active");
        var pending = CreatePending("session-pending", "com.example.pending");
        var state = new HiddenAppSessionStoreState(active, [pending], []);

        var prepared = state.PrepareForScreenLock(Now);

        Assert.Null(prepared.ActiveSession);
        Assert.Equal(2, prepared.PendingHides.Length);
        Assert.Contains(prepared.PendingHides, item => item.Session.SessionId == active.SessionId);
        Assert.Contains(prepared.PendingHides, item => item.Session.SessionId == pending.Session.SessionId);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]
    [InlineData(7, 30)]
    public void GetDelay_uses_capped_exponential_backoff(int failedAttempts, int expectedSeconds)
    {
        var delay = HiddenAppHideRetryPolicy.GetDelay(failedAttempts);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }

    private static HiddenAppPendingHideState CreatePending(string sessionId, string packageName)
    {
        return new HiddenAppPendingHideState(
            CreateSession(sessionId, packageName),
            "test",
            0,
            Now.ToUnixTimeMilliseconds());
    }

    private static HiddenAppSessionState CreateSession(string sessionId, string packageName)
    {
        return new HiddenAppSessionState(
            sessionId,
            packageName,
            packageName,
            42,
            Now.ToUnixTimeMilliseconds(),
            AndroidAppLaunchResult.CommandReceived(packageName, packageName));
    }
}
