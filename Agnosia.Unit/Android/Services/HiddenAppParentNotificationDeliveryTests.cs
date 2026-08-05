using Agnosia.Android.Api.Commands;
using Agnosia.Android.Services;
using Xunit;

namespace Agnosia.Unit.Android.Services;

public sealed class HiddenAppParentNotificationDeliveryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    // Ловит удаление durable callback до подтверждённого ответа personal profile.
    [Fact]
    public void ApplyResult_failure_preserves_notification_and_schedules_retry()
    {
        var state = CreatePendingNotification();

        var updated = HiddenAppParentNotificationDelivery.ApplyResult(
            state,
            "session-a",
            deliverySucceeded: false,
            Now);

        var notification = Assert.Single(updated.PendingParentNotifications);
        Assert.Equal("session-a", notification.Session.SessionId);
        Assert.Equal(1, notification.FailedAttempts);
        Assert.Equal(Now.AddSeconds(1).ToUnixTimeMilliseconds(), notification.NextAttemptAtUnixTimeMilliseconds);
    }

    // Ловит бесконечную повторную доставку после подтверждённой personal-side обработки.
    [Fact]
    public void ApplyResult_success_removes_matching_notification()
    {
        var state = CreatePendingNotification();

        var updated = HiddenAppParentNotificationDelivery.ApplyResult(
            state,
            "session-a",
            deliverySucceeded: true,
            Now);

        Assert.True(updated.IsEmpty);
    }

    private static HiddenAppSessionStoreState CreatePendingNotification()
    {
        var session = new HiddenAppSessionState(
            "session-a",
            "com.example.a",
            "Example",
            42,
            Now.ToUnixTimeMilliseconds(),
            AndroidAppLaunchResult.CommandReceived("com.example.a", "Example"))
        {
            ParentCallbackLaunchId = "launch-a"
        };
        return HiddenAppSessionStoreState.Empty
            .StartOrReplace(session, Now)
            .BeginCompletion(session.SessionId, "target_inactive", Now)
            .ConfirmHidden(session.SessionId, Now);
    }
}
