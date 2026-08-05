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

    private sealed record UsageObservationSnapshot(
        bool IsForeground,
        bool ConfirmedInactive,
        bool SawTargetForeground,
        string? TopPackage,
        bool IsSystemDelegatedFlow,
        DateTimeOffset? InactiveSince,
        string? LatestTargetEventName,
        long LatestTargetEventAt,
        string? LatestTargetClassName,
        string? LatestForegroundPackage,
        string? LatestForegroundEventName,
        string? LatestForegroundClassName,
        long LatestForegroundAt,
        string TargetUsageEvents,
        int ScannedEvents,
        int TargetEvents,
        int ForegroundEvents,
        string Reason);

}
