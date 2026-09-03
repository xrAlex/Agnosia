using Agnosia.Android.Api.Commands;
using Agnosia.Android.Commands;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class WorkProfileLaunchPreflightTests
{
    public static TheoryData<bool, bool?, bool, AndroidAppLaunchIssueKind> BlockedProfiles => new()
    {
        {
            false, false, false,
            AndroidAppLaunchIssueKind.WorkProfileUnavailable
        },
        {
            true, true, true,
            AndroidAppLaunchIssueKind.QuietMode
        },
        {
            true, null, true,
            AndroidAppLaunchIssueKind.WorkProfileUnavailable
        },
        { true, false, false, AndroidAppLaunchIssueKind.WorkProfileUnavailable }
    };

    // Ловит перенос VPN takeover перед проверкой quiet/profile/command target.
    [Theory]
    [MemberData(nameof(BlockedProfiles))]
    public void Evaluate_rejects_unavailable_work_profile_before_launch(
        bool managedProfileExists,
        bool? quietModeEnabled,
        bool commandTargetResolvable,
        AndroidAppLaunchIssueKind expectedIssue)
    {
        var launch = AndroidAppLaunchResult.CommandReceived("com.example.hidden", "Hidden");
        var availability = new WorkProfileLaunchAvailability(
            managedProfileExists,
            quietModeEnabled,
            commandTargetResolvable);

        var failure = WorkProfileLaunchPreflight.Evaluate(launch, availability);

        Assert.NotNull(failure);
        Assert.False(failure.Succeeded);
        Assert.Equal(expectedIssue, failure.Issue);
        Assert.Equal(AndroidAppLaunchStage.CommandReceived, failure.Stage);
    }

    // Ловит fail-open отказ при полностью готовом work-профиле.
    [Fact]
    public void Evaluate_allows_ready_work_profile_with_dpm_command_target()
    {
        var launch = AndroidAppLaunchResult.CommandReceived("com.example.hidden", "Hidden");
        var availability = new WorkProfileLaunchAvailability(
            true,
            false,
            true);

        var failure = WorkProfileLaunchPreflight.Evaluate(launch, availability);

        Assert.Null(failure);
    }
}
