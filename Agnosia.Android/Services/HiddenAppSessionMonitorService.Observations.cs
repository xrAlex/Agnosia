namespace Agnosia.Android.Services;

public sealed partial class HiddenAppSessionMonitorService
{
    private sealed record UsageSessionObservation(
        bool IsForeground,
        bool ConfirmedInactive,
        bool SawTargetForeground,
        DateTimeOffset? InactiveSince,
        string? TopPackage,
        bool IsSystemDelegatedFlow);

}
