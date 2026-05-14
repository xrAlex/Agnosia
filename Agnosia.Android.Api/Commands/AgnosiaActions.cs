namespace Agnosia.Android.Api;

public static class AgnosiaActions
{
    public const string FinalizeProvision = "agnosia.action.FINALIZE_PROVISION";
    public const string ProfilePing = "agnosia.action.TRY_START_SERVICE";
    public const string QueryApps = "agnosia.action.QUERY_APPS";
    public const string QueryAppIcon = "agnosia.action.QUERY_APP_ICON";
    public const string QueryAppIcons = "agnosia.action.QUERY_APP_ICONS";
    public const string QueryLogs = "agnosia.action.QUERY_LOGS";
    public const string QueryCrossProfilePackages = "agnosia.action.QUERY_CROSS_PROFILE_PACKAGES";
    public const string QueryUsageStatsAccess = "agnosia.action.QUERY_USAGE_STATS_ACCESS";
    public const string RequestUsageStatsAccess = "agnosia.action.REQUEST_USAGE_STATS_ACCESS";
    public const string QueryPackageInstallAccess = "agnosia.action.QUERY_PACKAGE_INSTALL_ACCESS";
    public const string RequestPackageInstallAccess = "agnosia.action.REQUEST_PACKAGE_INSTALL_ACCESS";
    public const string InstallPackage = "agnosia.action.INSTALL_PACKAGE";
    public const string UninstallPackage = "agnosia.action.UNINSTALL_PACKAGE";
    public const string FreezePackage = "agnosia.action.FREEZE_PACKAGE";
    public const string UnfreezePackage = "agnosia.action.UNFREEZE_PACKAGE";
    public const string UnfreezeAndLaunch = "agnosia.action.UNFREEZE_AND_LAUNCH";
    public const string PrepareHiddenShortcut = "agnosia.action.PREPARE_HIDDEN_SHORTCUT";
    public const string CreateHiddenShortcut = "agnosia.action.CREATE_HIDDEN_SHORTCUT";
    public const string LaunchHiddenAppShortcut = "agnosia.action.LAUNCH_HIDDEN_APP_SHORTCUT";
    public const string LaunchAppProxy = "agnosia.action.LAUNCH_APP_PROXY";
    public const string ShortcutPinned = "agnosia.action.SHORTCUT_PINNED";
    public const string SetCrossProfileInteraction = "agnosia.action.SET_CROSS_PROFILE_INTERACTION";
    public const string SynchronizePreference = "agnosia.action.SYNCHRONIZE_PREFERENCE";
    public const string WorkAppFrozen = "agnosia.action.WORK_APP_FROZEN";
    public const string PackageInstallerCallback = "agnosia.action.PACKAGEINSTALLER_CALLBACK";

    public static readonly string[] ParentToManagedCommandActions =
    [
        ProfilePing,
        QueryApps,
        QueryAppIcon,
        QueryAppIcons,
        QueryLogs,
        QueryCrossProfilePackages,
        QueryUsageStatsAccess,
        RequestUsageStatsAccess,
        QueryPackageInstallAccess,
        RequestPackageInstallAccess,
        InstallPackage,
        UninstallPackage,
        FreezePackage,
        UnfreezePackage,
        UnfreezeAndLaunch,
        PrepareHiddenShortcut,
        CreateHiddenShortcut,
        LaunchAppProxy,
        SetCrossProfileInteraction,
        SynchronizePreference
    ];

    public static readonly string[] ManagedToParentCommandActions =
    [
        WorkAppFrozen,
        FinalizeProvision
    ];

    public static readonly string[] TargetProfileActivityActions =
    [
        FinalizeProvision,
        ProfilePing,
        QueryApps,
        QueryAppIcon,
        QueryAppIcons,
        QueryLogs,
        QueryCrossProfilePackages,
        QueryUsageStatsAccess,
        RequestUsageStatsAccess,
        QueryPackageInstallAccess,
        RequestPackageInstallAccess,
        InstallPackage,
        UninstallPackage,
        FreezePackage,
        UnfreezePackage,
        PrepareHiddenShortcut,
        CreateHiddenShortcut,
        UnfreezeAndLaunch,
        LaunchAppProxy,
        SetCrossProfileInteraction,
        SynchronizePreference,
        WorkAppFrozen,
        PackageInstallerCallback
    ];

    public static readonly string[] LocalOnlyTargetProfileActivityActions =
    [
        PackageInstallerCallback
    ];
}
