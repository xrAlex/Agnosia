namespace Agnosia.Android.Api.Storage;

public static class StorageKeys
{
    public const string IsSettingUp = "is_setting_up";
    public const string HasSetup = "has_setup";
    public const string SetupStartedAtUtc = "setup_started_at_utc";
    public const string ManagedProfileProvisionedAtUtc = "managed_profile_provisioned_at_utc";
    public const string ManagedProfileUserHandle = "managed_profile_user_handle";
    public const string ManagedProfileUserSerial = "managed_profile_user_serial";
    public const string AuthKey = "auth_key";
    public const string ShowAllApps = "show_all_apps";
    public const string DisableVpnBeforeWorkLaunch = "disable_vpn_before_work_launch";
    public const string CrossProfileFileShuttleEnabled = "cross_profile_file_shuttle_enabled";
    public const string LockdownEnabled = "lockdown_enabled";
    public const string LockdownBlockedPackages = "lockdown_blocked_packages";
    public const string RiskEngineEnabled = "risk_engine_enabled";
    public const string EnableVpnAfterWorkFreeze = "enable_vpn_after_work_freeze";
    public const string VpnAfterWorkFreezeClient = "vpn_after_work_freeze_client";
    public const string TunguskaAutomationToken = "tunguska_automation_token";
    public const string LoggingEnabled = "logging_enabled";
    public const string AppTheme = "app_theme";
    public const string UsageStatsAccessPrompted = "usage_stats_access_prompted";
    public const string OnboardingCompleted = "onboarding_completed";
    public const string LogEntries = "log_entries";
    public const string HiddenShortcutMetadataPrefix = "hidden_shortcut_metadata:";
    public const string HiddenAppActiveSession = "hidden_app_active_session";
    public const string HaveActiveVpnSession = "have_active_vpn_session";
}
