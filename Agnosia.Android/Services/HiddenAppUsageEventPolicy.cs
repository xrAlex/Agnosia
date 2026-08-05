using System.Globalization;

namespace Agnosia.Android.Services;

internal static class HiddenAppUsageEventPolicy
{
    private const int ActivityResumed = 1;
    private const int ActivityPaused = 2;
    private const int ActivityStopped = 23;
    private const int ActivityDestroyed = 24;

    public static bool IsForeground(int eventType)
    {
        return eventType == ActivityResumed;
    }

    public static bool IsLifecycleTransition(int eventType)
    {
        return eventType is ActivityResumed or ActivityPaused or ActivityStopped or ActivityDestroyed;
    }

    public static bool IsConfirmedInvisible(int eventType)
    {
        return eventType is ActivityStopped or ActivityDestroyed;
    }

    public static bool CanStartDelegatedFlow(int eventType)
    {
        return eventType is ActivityPaused or ActivityStopped or ActivityDestroyed;
    }

    public static string GetName(int eventType)
    {
        return eventType switch
        {
            ActivityResumed => "ACTIVITY_RESUMED",
            ActivityPaused => "ACTIVITY_PAUSED",
            ActivityStopped => "ACTIVITY_STOPPED",
            ActivityDestroyed => "ACTIVITY_DESTROYED",
            _ => eventType.ToString(CultureInfo.InvariantCulture)
        };
    }
}
