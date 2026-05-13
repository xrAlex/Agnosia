using Agnosia.Models;

namespace Agnosia.ViewModels;

internal static class DashboardStatusTextFormatter
{
    public static string GetWorkProfileStatus(
        WorkProfileStateKind workProfileState,
        bool workProfileAvailable,
        bool hasSetup) =>
        workProfileAvailable
            ? "Available"
            : workProfileState switch
            {
                WorkProfileStateKind.ProvisioningInProgress => "Pending",
                WorkProfileStateKind.WorkProfileQuietMode => "QuietMode",
                WorkProfileStateKind.WorkProfileUnavailable => "Disabled",
                WorkProfileStateKind.WorkProfileCommandTargetUnavailable
                    or WorkProfileStateKind.WorkProfileCommandChannelUnavailable => "CommandIssue",
                _ => hasSetup ? "Unavailable" : "NotCreated"
            };

    public static string GetOverviewHeadline(
        bool hasLoadedSnapshot,
        bool isSupported,
        bool hasSetup,
        bool isBusy,
        bool workProfileAvailable,
        WorkProfileStateKind workProfileState) =>
        !hasLoadedSnapshot
            ? "Loading"
            : !isSupported
                ? "NotSupported"
                : !hasSetup
                    ? "NotSetup"
                    : isBusy
                        ? "Syncing"
                        : workProfileAvailable
                            ? "Active"
                            : GetUnavailableHeadline(workProfileState);

    public static string GetOverallStatusText(
        bool statusIsError,
        bool hasLoadedSnapshot,
        bool isBusy,
        bool isSupported,
        bool hasSetup,
        bool workProfileAvailable) =>
        statusIsError
            ? "Error"
            : !hasLoadedSnapshot || isBusy
                ? "Updating"
                : !isSupported || !hasSetup || !workProfileAvailable
                    ? "Unstable"
                    : "Ok";

    public static string GetOverallStatusCaption(
        bool statusIsError,
        bool hasLoadedSnapshot,
        bool isBusy,
        bool isSupported,
        bool hasSetup,
        bool workProfileAvailable,
        WorkProfileStateKind workProfileState) =>
        statusIsError
            ? "Error"
            : !hasLoadedSnapshot
                ? "Loading"
                : isBusy
                    ? "Syncing"
                    : !isSupported
                        ? "NotSupported"
                        : !hasSetup
                            ? "NotSetup"
                            : !workProfileAvailable
                                ? GetUnavailableDetail(workProfileState)
                                : "Ok";

    private static string GetUnavailableHeadline(WorkProfileStateKind workProfileState) =>
        workProfileState switch
        {
            WorkProfileStateKind.WorkProfileQuietMode => "WPQuietMode",
            WorkProfileStateKind.WorkProfileUnavailable => "WPDisabled",
            WorkProfileStateKind.WorkProfileCommandTargetUnavailable
                or WorkProfileStateKind.WorkProfileCommandChannelUnavailable => "WPCommandIssue",
            _ => "WPUnavailable"
        };

    private static string GetUnavailableDetail(WorkProfileStateKind workProfileState) =>
        workProfileState switch
        {
            WorkProfileStateKind.WorkProfileQuietMode => "WPQuietMode",
            WorkProfileStateKind.WorkProfileUnavailable => "WPDisabled",
            WorkProfileStateKind.WorkProfileCommandTargetUnavailable
                or WorkProfileStateKind.WorkProfileCommandChannelUnavailable => "WPCommandIssue",
            _ => "WPUnavailable"
        };
}
