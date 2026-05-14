namespace Agnosia.Android.Api.Commands;

public static class AndroidCommandContract
{
    public const string ResultAppsJson = "apps_json";
    public const string ResultIconPng = "icon_png";
    public const string ResultIconsBundle = "icons_bundle";
    public const string ResultLogsJson = "logs_json";
    public const string ResultInteractionPackages = "interaction_packages";
    public const string ResultUsageStatsAccess = "usage_stats_access";
    public const string ResultPackageInstallAccess = "package_install_access";
    public const string ResultMessage = "message";
    public const string ResultProfileOwnerCheckPerformed = "profile_owner_check_performed";
    public const string ResultIsProfileOwner = "is_profile_owner";
    public const string ResultHideImmediately = "hide_immediately";
    public const string ResultLaunchJson = "launch_json";
    public const string ResultToggleSuccess = "toggle_success";
    public const string ResultError = "error";
    public const string ExtraParentFrozenCallback = "parent_frozen_callback";
    public const string ExtraCallbackPackage = "callback_package";
    public const string ExtraCallbackSignature = "callback_signature";
    public const string ExtraPackageInstallerOperation = "package_installer_operation";
    public const string PackageInstallerOperationInstall = "install";
    public const string PackageInstallerOperationUninstall = "uninstall";
    public const string ErrorSystemAppUnsupported = "system_app_unsupported";
}