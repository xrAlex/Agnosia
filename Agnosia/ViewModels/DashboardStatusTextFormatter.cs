using Agnosia.Models;

namespace Agnosia.ViewModels;

internal static class DashboardStatusTextFormatter
{
    public static string GetWorkProfileStatus(
        WorkProfileStateKind workProfileState,
        bool workProfileAvailable,
        bool hasSetup)
    {
        return workProfileAvailable
            ? "Available"
            : workProfileState switch
            {
                WorkProfileStateKind.ProvisioningInProgress => "Pending",
                WorkProfileStateKind.WorkProfileQuietMode => "QuietMode",
                WorkProfileStateKind.WorkProfileUnavailable => "Disabled",
                _ => hasSetup ? "Unavailable" : "NotCreated"
            };
    }

    public static string GetOverviewHeadline(
        bool hasLoadedSnapshot,
        bool isSupported,
        bool hasSetup,
        bool isBusy,
        bool workProfileAvailable,
        WorkProfileStateKind workProfileState)
    {
        return !hasLoadedSnapshot
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
    }

    public static string GetOverallStatusText(
        bool statusIsError,
        bool hasLoadedSnapshot,
        bool isBusy,
        bool isSupported,
        bool hasSetup,
        bool workProfileAvailable)
    {
        return statusIsError
            ? "Error"
            : !hasLoadedSnapshot || isBusy
                ? "Updating"
                : !isSupported || !hasSetup || !workProfileAvailable
                    ? "Unstable"
                    : "Ok";
    }

    public static string GetOverallStatusCaption(
        bool statusIsError,
        bool hasLoadedSnapshot,
        bool isBusy,
        bool isSupported,
        bool hasSetup,
        bool workProfileAvailable,
        WorkProfileStateKind workProfileState)
    {
        return statusIsError
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
    }

    private static string GetUnavailableHeadline(WorkProfileStateKind workProfileState)
    {
        return workProfileState switch
        {
            WorkProfileStateKind.WorkProfileQuietMode => "WPQuietMode",
            WorkProfileStateKind.WorkProfileUnavailable => "WPDisabled",
            _ => "WPUnavailable"
        };
    }

    private static string GetUnavailableDetail(WorkProfileStateKind workProfileState)
    {
        return workProfileState switch
        {
            WorkProfileStateKind.WorkProfileQuietMode => "WPQuietMode",
            WorkProfileStateKind.WorkProfileUnavailable => "WPDisabled",
            _ => "WPUnavailable"
        };
    }
}
