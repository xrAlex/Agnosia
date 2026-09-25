using Agnosia.Android.Api.Commands;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class ActivityResumeRefreshPolicyTests
{
    [Theory]
    [InlineData(AgnosiaActions.QueryApps)]
    [InlineData(AgnosiaActions.QueryAppIcon)]
    [InlineData(AgnosiaActions.QueryPermissions)]
    [InlineData(AgnosiaActions.ProfilePing)]
    [InlineData(AgnosiaActions.ConnectCommandProvider)]
    public void Returning_from_internal_read_does_not_refresh_dashboard(string action)
    {
        var policy = new ActivityResumeRefreshPolicy();

        policy.RecordActivityStart(action);

        Assert.False(policy.ShouldRefreshDashboardOnResume());
        Assert.True(policy.ShouldRefreshDashboardOnResume());
    }

    [Theory]
    [InlineData("android.settings.APPLICATION_DETAILS_SETTINGS")]
    [InlineData(AgnosiaActions.RevokeRuntimePermissions)]
    [InlineData(null)]
    public void Returning_from_external_or_mutating_activity_refreshes_dashboard(string? action)
    {
        var policy = new ActivityResumeRefreshPolicy();

        policy.RecordActivityStart(action);

        Assert.True(policy.ShouldRefreshDashboardOnResume());
    }

    [Fact]
    public void Internal_result_without_activity_pause_does_not_suppress_later_resume()
    {
        var policy = new ActivityResumeRefreshPolicy();
        policy.RecordActivityStart(AgnosiaActions.QueryApps);

        policy.RecordActivityResult(activityIsResumed: true);

        Assert.True(policy.ShouldRefreshDashboardOnResume());
    }
}
