namespace Agnosia.Android.Api.Commands;

public static class AndroidCommandContract
{
    public const string FileShuttleDocumentsProviderComponent =
        "com.agnosia.app.AgnosiaCrossProfileDocumentsProvider";

    public const string ExtraPackage = "package";
    public const string ExtraPackages = "packages";
    public const string ExtraPermissions = "permissions";
    public const string ExtraIsSystem = "is_system";
    public const string ExtraApk = "apk";
    public const string ExtraSplitApks = "split_apks";
    public const string ExtraShowAll = "show_all";
    public const string ExtraPreferenceName = "name";
    public const string ExtraPreferenceBoolean = "boolean";
    public const string ExtraInternetBlocked = "internet_blocked";
    public const string ExtraFileShuttleCallbackMessenger = "file_shuttle_callback_messenger";
    public const string ExtraLaunchPackageName = "packageName";
    public const string ExtraLaunchDisplayName = "displayName";
    public const string ExtraShortcutTargetActivity = "targetActivity";
    public const string ExtraShortcutLabel = "label";
    public const string ExtraShortcutIconBase64 = "iconBase64";
    public const string ExtraShortcutToken = "shortcutToken";
    public const string ExtraTrigger = "trigger";
    public const string ExtraReplacementAuthKey = "replacement_auth_key";
    public const string ExtraQueryOffset = "query_offset";
    public const string ExtraQueryLimit = "query_limit";
    public const string ExtraQueryMaxJsonBytes = "query_max_json_bytes";
    public const string ExtraQueryPageToken = "query_page_token";
    public const string ResultAppsJson = "apps_json";
    public const string ResultNextQueryOffset = "next_query_offset";
    public const string ResultQueryHasMore = "query_has_more";
    public const string ResultQueryTotalCount = "query_total_count";
    public const string ResultIconPng = "icon_png";
    public const string ResultIconsBundle = "icons_bundle";
    public const string ResultLogsJson = "logs_json";
    public const string ResultInteractionPackages = "interaction_packages";
    public const string ResultUsageStatsAccess = "usage_stats_access";
    public const string ResultPackageInstallAccess = "package_install_access";
    public const string ResultAllFilesAccess = "all_files_access";
    public const string ResultMessage = "message";
    public const string ResultProfileOwnerCheckPerformed = "profile_owner_check_performed";
    public const string ResultIsProfileOwner = "is_profile_owner";
    public const string ResultAppVersionCode = "app_version_code";
    public const string ResultAppVersionName = "app_version_name";
    public const string ResultHideImmediately = "hide_immediately";
    public const string ResultPreHideSucceeded = "pre_hide_succeeded";
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
