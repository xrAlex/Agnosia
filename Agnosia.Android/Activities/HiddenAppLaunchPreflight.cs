using Agnosia.Android.Api.Commands;

namespace Agnosia.Android.Activities;

internal static class HiddenAppLaunchPreflight
{
    public static HiddenAppLaunchPreflightResult RequireUsageAccess(
        AndroidAppLaunchResult launchResult,
        bool hasUsageAccess)
    {
        return hasUsageAccess
            ? new HiddenAppLaunchPreflightResult(true, launchResult)
            : new HiddenAppLaunchPreflightResult(
                false,
                launchResult.Fail(
                    AndroidAppLaunchStage.CommandReceived,
                    AndroidAppLaunchIssueKind.UsageAccessDenied,
                    "usageStatsAccess=denied"));
    }
}

internal sealed record HiddenAppLaunchPreflightResult(
    bool CanProceed,
    AndroidAppLaunchResult LaunchResult);
