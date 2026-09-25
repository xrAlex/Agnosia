namespace Agnosia.Android.Api.Commands;

public sealed class ActivityResumeRefreshPolicy
{
    private bool _skipNextRefresh;

    public void RecordActivityStart(string? action)
    {
        _skipNextRefresh = action is not null &&
                           (action.StartsWith("agnosia.action.QUERY_", StringComparison.Ordinal)
                            || action == AgnosiaActions.ProfilePing
                            || action == AgnosiaActions.ConnectCommandProvider);
    }

    public void RecordActivityResult(bool activityIsResumed)
    {
        if (activityIsResumed) _skipNextRefresh = false;
    }

    public bool ShouldRefreshDashboardOnResume()
    {
        var shouldRefresh = !_skipNextRefresh;
        _skipNextRefresh = false;
        return shouldRefresh;
    }
}
