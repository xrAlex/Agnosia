using Agnosia.Android.Api.Commands;
using Android.Content;

namespace Agnosia.Android.Commands;

internal static partial class WorkProfileLaunchPreflight
{
    public static AndroidAppLaunchResult? TryCreateFailure(
        Context context,
        AndroidAppLaunchResult launchResult)
    {
        try
        {
            var diagnostics = AndroidWorkProfileDiagnosticsReader.Read(context);
            var crossProfileApps = AndroidSystemApi.GetCrossProfileApps(context);
            var canInteractAcrossProfiles = diagnostics.AvailableToCrossProfileApps
                                            && crossProfileApps?.CanInteractAcrossProfiles() == true;
            var availability = new WorkProfileLaunchAvailability(
                diagnostics.ManagedProfileExists,
                diagnostics.QuietModeEnabled,
                canInteractAcrossProfiles,
                diagnostics.CommandTargetResolvable);
            return Evaluate(launchResult, availability);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            return launchResult.Fail(
                AndroidAppLaunchStage.CommandReceived,
                AndroidAppLaunchIssueKind.WorkProfileUnavailable,
                $"preflight={exception.GetType().Name}");
        }
    }
}
