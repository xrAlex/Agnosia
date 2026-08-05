using Agnosia.Android.Activities;
using Agnosia.Android.Api.Commands;
using Xunit;

namespace Agnosia.Unit.Android.Activities;

public sealed class HiddenAppLaunchPreflightTests
{
    // Проверяет запрет запуска до любой успешной стадии без Usage Access.
    [Fact]
    public void RequireUsageAccess_rejects_launch_before_any_success_stage_when_access_is_missing()
    {
        var launch = AndroidAppLaunchResult.CommandReceived("com.example.hidden", "Hidden");

        var result = HiddenAppLaunchPreflight.RequireUsageAccess(launch, hasUsageAccess: false);

        Assert.False(result.CanProceed);
        Assert.False(result.LaunchResult.Succeeded);
        Assert.Equal(AndroidAppLaunchStage.CommandReceived, result.LaunchResult.Stage);
        Assert.Equal(AndroidAppLaunchIssueKind.UsageAccessDenied, result.LaunchResult.Issue);
    }

    // Проверяет неизменность launch result при выданном Usage Access.
    [Fact]
    public void RequireUsageAccess_preserves_launch_result_when_access_is_granted()
    {
        var launch = AndroidAppLaunchResult.CommandReceived("com.example.hidden", "Hidden");

        var result = HiddenAppLaunchPreflight.RequireUsageAccess(launch, hasUsageAccess: true);

        Assert.True(result.CanProceed);
        Assert.Same(launch, result.LaunchResult);
    }
}
