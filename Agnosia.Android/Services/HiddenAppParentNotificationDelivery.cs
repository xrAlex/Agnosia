namespace Agnosia.Android.Services;

internal static class HiddenAppParentNotificationDelivery
{
    public static HiddenAppSessionStoreState ApplyResult(
        HiddenAppSessionStoreState state,
        string sessionId,
        bool deliverySucceeded,
        DateTimeOffset now)
    {
        return deliverySucceeded
            ? state.ConfirmParentNotification(sessionId)
            : state.RecordParentNotificationFailure(sessionId, now);
    }
}
