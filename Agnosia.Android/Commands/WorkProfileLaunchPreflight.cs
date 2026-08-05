using Agnosia.Android.Api.Commands;

namespace Agnosia.Android.Commands;

internal static partial class WorkProfileLaunchPreflight
{
    public static AndroidAppLaunchResult? Evaluate(
        AndroidAppLaunchResult launchResult,
        WorkProfileLaunchAvailability availability)
    {
        if (availability.QuietModeEnabled == true)
            return launchResult.Fail(
                AndroidAppLaunchStage.CommandReceived,
                AndroidAppLaunchIssueKind.QuietMode,
                "quietMode=true");

        if (availability.ManagedProfileExists
            && availability.QuietModeEnabled == false
            && availability.CanInteractAcrossProfiles
            && availability.CommandTargetResolvable)
            return null;

        return launchResult.Fail(
            AndroidAppLaunchStage.CommandReceived,
            AndroidAppLaunchIssueKind.WorkProfileUnavailable,
            $"managedProfileExists={availability.ManagedProfileExists}; " +
            $"quietMode={availability.QuietModeEnabled?.ToString() ?? "unknown"}; " +
            $"canInteractAcrossProfiles={availability.CanInteractAcrossProfiles}; " +
            $"commandTargetResolvable={availability.CommandTargetResolvable}");
    }
}

internal sealed record WorkProfileLaunchAvailability(
    bool ManagedProfileExists,
    bool? QuietModeEnabled,
    bool CanInteractAcrossProfiles,
    bool CommandTargetResolvable);
