using Agnosia.Android.Api.Permissions;
using Agnosia.Models;
using Xunit;

namespace Agnosia.Unit.AndroidApi.Permissions;

public sealed class AppPermissionRiskCatalogTests
{
    [Fact]
    public void Classify_returns_safe_when_no_combination_rule_matches()
    {
        var result = AppPermissionRiskCatalog.Classify([
            "android.permission.VIBRATE",
            "android.permission.INTERNET"
        ]);

        Assert.Equal(AppPermissionRiskLevel.Safe, result);
    }

    [Fact]
    public void Analyze_returns_permission_lists_for_safe_result()
    {
        var result = AppPermissionRiskCatalog.Analyze([
            "android.permission.VIBRATE",
            "android.permission.INTERNET"
        ]);

        Assert.Equal(AppPermissionRiskLevel.Safe, result.Level);
        Assert.Equal(
            [
                "android.permission.VIBRATE",
                "android.permission.INTERNET"
            ],
            result.ManifestPermissions);
        Assert.Empty(result.RuntimePermissions);
    }

    [Fact]
    public void Classify_returns_dangerous_for_declared_location_network_combination()
    {
        var result = AppPermissionRiskCatalog.Classify([
            "android.permission.ACCESS_FINE_LOCATION",
            "android.permission.INTERNET"
        ]);

        Assert.Equal(AppPermissionRiskLevel.Dangerous, result);
    }

    [Fact]
    public void Classify_returns_critical_for_background_location_network_combination()
    {
        var result = AppPermissionRiskCatalog.Classify([
            "android.permission.ACCESS_FINE_LOCATION",
            "android.permission.ACCESS_BACKGROUND_LOCATION",
            "android.permission.INTERNET"
        ]);

        Assert.Equal(AppPermissionRiskLevel.Critical, result);
    }

    [Fact]
    public void Analyze_returns_matched_permissions_without_duplicates()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.VIBRATE",
                "android.permission.INTERNET",
                "android.permission.CAMERA",
                "android.permission.INTERNET"
            ],
            ForegroundServiceTypes: ["camera"]));

        Assert.Equal(AppPermissionRiskLevel.Critical, result.Level);
        Assert.Equal(
            [
                "android.permission.INTERNET",
                "android.permission.CAMERA"
            ],
            result.RiskyPermissions);
    }

    [Fact]
    public void Analyze_flags_legacy_read_external_storage_on_supported_sdks()
    {
        var android12Result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.READ_EXTERNAL_STORAGE",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 32));
        var android13Result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.READ_EXTERNAL_STORAGE",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 33));

        Assert.Equal(AppPermissionRiskLevel.Dangerous, android12Result.Level);
        Assert.Equal(AppPermissionRiskLevel.Safe, android13Result.Level);
    }

    [Fact]
    public void Analyze_flags_declared_granular_media_permissions_as_dangerous()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.READ_MEDIA_IMAGES",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 33));

        Assert.Equal(AppPermissionRiskLevel.Dangerous, result.Level);
    }

    [Fact]
    public void Analyze_flags_media_with_location_metadata_as_dangerous()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.READ_MEDIA_IMAGES",
                "android.permission.ACCESS_MEDIA_LOCATION",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 33,
            GrantedPermissions:
            [
                "android.permission.READ_MEDIA_IMAGES",
                "android.permission.ACCESS_MEDIA_LOCATION"
            ]));

        Assert.Equal(AppPermissionRiskLevel.Critical, result.Level);
        Assert.Equal(
            [
                "SU-MEDIA-IMG-01",
                "SU-MEDIA-LOC-IMG-01"
            ],
            result.MatchedRuleIds);
        Assert.Equal(12, result.Score);
        Assert.Equal(12, result.RawScore);
        Assert.Equal(AppPermissionRiskConfidence.High, result.Confidence);
    }

    [Fact]
    public void Analyze_treats_visual_user_selected_as_low_signal_on_android_14()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.READ_MEDIA_VISUAL_USER_SELECTED",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 34));

        Assert.Equal(AppPermissionRiskLevel.Safe, result.Level);
    }

    [Fact]
    public void Analyze_keeps_media_projection_declaration_dangerous_without_observed_capture()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.FOREGROUND_SERVICE_MEDIA_PROJECTION",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 34,
            ForegroundServiceTypes: ["mediaProjection"]));

        Assert.Equal(AppPermissionRiskLevel.Dangerous, result.Level);
        Assert.Contains("SU-SCR-FGS-01", result.MatchedRuleIds);
        Assert.DoesNotContain("CR-SCR-01", result.MatchedRuleIds);
    }

    [Fact]
    public void Analyze_flags_observed_media_projection_with_exfiltration_as_critical()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.FOREGROUND_SERVICE_MEDIA_PROJECTION",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 34,
            ForegroundServiceTypes: ["mediaProjection"],
            ObservedSignals: ["android.observed.MediaProjection"]));

        Assert.Equal(AppPermissionRiskLevel.Critical, result.Level);
        Assert.Contains("CR-SCR-01", result.MatchedRuleIds);
    }

    [Fact]
    public void Analyze_uses_foreground_service_type_on_android_12_and_13()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.RECORD_AUDIO",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 32,
            ForegroundServiceTypes: ["microphone"]));

        Assert.Equal(AppPermissionRiskLevel.Critical, result.Level);
    }

    [Fact]
    public void Analyze_uses_android_14_type_specific_foreground_service_permission()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.RECORD_AUDIO",
                "android.permission.FOREGROUND_SERVICE_MICROPHONE",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 34));

        Assert.Equal(AppPermissionRiskLevel.Critical, result.Level);
        Assert.Equal(
            [
                "android.permission.RECORD_AUDIO",
                "android.permission.FOREGROUND_SERVICE_MICROPHONE",
                "android.permission.INTERNET"
            ],
            result.RiskyPermissions);
    }

    [Fact]
    public void Analyze_does_not_treat_appop_blocked_runtime_permission_as_critical()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.RECORD_AUDIO",
                "android.permission.FOREGROUND_SERVICE_MICROPHONE",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 34,
            GrantedPermissions: ["android.permission.RECORD_AUDIO"],
            IsMicrophoneAppOpAllowed: false));

        Assert.Equal(AppPermissionRiskLevel.Safe, result.Level);
    }

    [Fact]
    public void Analyze_uses_service_permission_control_surfaces()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.INTERNET",
                "android.permission.SYSTEM_ALERT_WINDOW"
            ],
            ServicePermissions: ["android.permission.BIND_ACCESSIBILITY_SERVICE"],
            IsAccessibilityServiceEnabled: true,
            CanDrawOverlays: true));

        Assert.Equal(AppPermissionRiskLevel.Critical, result.Level);
        Assert.Equal(
            [
                "android.permission.INTERNET",
                "android.permission.SYSTEM_ALERT_WINDOW",
                "android.permission.BIND_ACCESSIBILITY_SERVICE"
            ],
            result.RiskyPermissions);
    }

    [Fact]
    public void Analyze_does_not_match_usage_stats_dangerous_rule_when_access_is_not_enabled()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.PACKAGE_USAGE_STATS",
                "android.permission.INTERNET"
            ]));

        Assert.Equal(AppPermissionRiskLevel.Safe, result.Level);
    }

    [Fact]
    public void Analyze_uses_enabled_special_access_as_standalone_signal()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [],
            HasUsageStatsAccess: true));

        Assert.Equal(AppPermissionRiskLevel.Dangerous, result.Level);
        Assert.Equal(["SU-PROF-USAGE-01"], result.MatchedRuleIds);
        Assert.Equal(["android.permission.PACKAGE_USAGE_STATS"], result.RiskyPermissions);
    }

    [Fact]
    public void Analyze_returns_all_matching_critical_rules()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.ACCESS_FINE_LOCATION",
                "android.permission.ACCESS_COARSE_LOCATION",
                "android.permission.ACCESS_BACKGROUND_LOCATION",
                "android.permission.INTERNET"
            ]));

        Assert.Equal(AppPermissionRiskLevel.Critical, result.Level);
        Assert.Equal(
            [
                "android.permission.ACCESS_FINE_LOCATION",
                "android.permission.ACCESS_COARSE_LOCATION",
                "android.permission.ACCESS_BACKGROUND_LOCATION",
                "android.permission.INTERNET"
            ],
            result.RiskyPermissions);
        Assert.Equal(
            [
                "CR-LOC-BG-01",
                "CR-LOC-BG-02"
            ],
            result.MatchedRuleIds);
        Assert.Equal(6, result.Score);
        Assert.Equal(12, result.RawScore);
        Assert.Equal(AppPermissionRiskConfidence.Medium, result.Confidence);
    }

    [Fact]
    public void Analyze_groups_overlapping_dangerous_rules_by_max_score()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.ACCESS_FINE_LOCATION",
                "android.permission.ACCESS_COARSE_LOCATION",
                "android.permission.INTERNET"
            ],
            GrantedPermissions:
            [
                "android.permission.ACCESS_FINE_LOCATION",
                "android.permission.ACCESS_COARSE_LOCATION"
            ]));

        Assert.Equal(AppPermissionRiskLevel.Dangerous, result.Level);
        Assert.Equal(5, result.Score);
        Assert.Equal(10, result.RawScore);
    }

    [Fact]
    public void Analyze_does_not_treat_unknown_runtime_grant_state_as_denied()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.RECORD_AUDIO",
                "android.permission.FOREGROUND_SERVICE_MICROPHONE",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 34,
            GrantedPermissions: ["android.permission.CAMERA"]));

        Assert.Equal(AppPermissionRiskLevel.Critical, result.Level);
        Assert.Equal(
            [
                "CR-MIC-FGS-14-01",
                "SU-MIC-01"
            ],
            result.MatchedRuleIds);
        Assert.Equal(AppPermissionRiskConfidence.High, result.Confidence);
    }

    [Fact]
    public void Analyze_treats_explicitly_denied_runtime_permission_as_ineffective()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.RECORD_AUDIO",
                "android.permission.FOREGROUND_SERVICE_MICROPHONE",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 34,
            DeniedPermissions: ["android.permission.RECORD_AUDIO"]));

        Assert.Equal(AppPermissionRiskLevel.Safe, result.Level);
    }

    [Fact]
    public void Analyze_flags_requested_only_legacy_write_external_storage_on_legacy_targets()
    {
        var legacyTargetResult = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.WRITE_EXTERNAL_STORAGE",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 32,
            TargetSdkVersion: 29));
        var modernTargetResult = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.WRITE_EXTERNAL_STORAGE",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 32,
            TargetSdkVersion: 30));

        Assert.Equal(AppPermissionRiskLevel.Dangerous, legacyTargetResult.Level);
        Assert.Equal(AppPermissionRiskLevel.Safe, modernTargetResult.Level);
    }

    [Fact]
    public void Analyze_flags_android_16_granular_health_reads_with_network()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.health.READ_HEART_RATE",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 36,
            TargetSdkVersion: 36));

        Assert.Equal(AppPermissionRiskLevel.Dangerous, result.Level);
        Assert.Equal(
            [
                "android.permission.health.READ_HEART_RATE",
                "android.permission.INTERNET"
            ],
            result.RiskyPermissions);
    }

    [Fact]
    public void Analyze_flags_android_15_background_health_reads_as_critical()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.health.READ_HEART_RATE",
                "android.permission.health.READ_HEALTH_DATA_IN_BACKGROUND",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 35,
            TargetSdkVersion: 35));

        Assert.Equal(AppPermissionRiskLevel.Critical, result.Level);
        Assert.Contains("CR-HEALTH-15-BG-01", result.MatchedRuleIds);
    }

    [Fact]
    public void Analyze_flags_android_16_background_health_reads_as_critical()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.health.READ_HEART_RATE",
                "android.permission.health.READ_HEALTH_DATA_IN_BACKGROUND",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 36,
            TargetSdkVersion: 36));

        Assert.Equal(AppPermissionRiskLevel.Critical, result.Level);
        Assert.Equal(
            [
                "android.permission.health.READ_HEART_RATE",
                "android.permission.health.READ_HEALTH_DATA_IN_BACKGROUND",
                "android.permission.INTERNET"
            ],
            result.RiskyPermissions);
    }

    [Fact]
    public void Analyze_keeps_body_sensors_legacy_for_pre_android_16_targets()
    {
        var legacyTargetResult = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.BODY_SENSORS",
                "android.permission.BODY_SENSORS_BACKGROUND",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 36,
            TargetSdkVersion: 35));
        var android16TargetResult = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.BODY_SENSORS",
                "android.permission.BODY_SENSORS_BACKGROUND",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 36,
            TargetSdkVersion: 36));

        Assert.Equal(AppPermissionRiskLevel.Critical, legacyTargetResult.Level);
        Assert.Equal(AppPermissionRiskLevel.Safe, android16TargetResult.Level);
    }

    [Fact]
    public void Analyze_flags_android_16_local_network_opt_in_shape()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.NEARBY_WIFI_DEVICES",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 36,
            TargetSdkVersion: 36));

        Assert.Equal(AppPermissionRiskLevel.Dangerous, result.Level);
    }

    [Fact]
    public void Analyze_flags_android_17_local_network_permission()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.ACCESS_LOCAL_NETWORK",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 37,
            TargetSdkVersion: 37));

        Assert.Equal(AppPermissionRiskLevel.Dangerous, result.Level);
        Assert.Equal(
            [
                "android.permission.ACCESS_LOCAL_NETWORK",
                "android.permission.INTERNET"
            ],
            result.RiskyPermissions);
    }

    [Fact]
    public void Analyze_flags_android_17_local_network_without_internet()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.ACCESS_LOCAL_NETWORK"
            ],
            DeviceSdkVersion: 37,
            TargetSdkVersion: 37));

        Assert.Equal(AppPermissionRiskLevel.Dangerous, result.Level);
        Assert.Equal(["SU-LAN-17-01"], result.MatchedRuleIds);
    }

    [Fact]
    public void Analyze_flags_apk_install_inventory_shape()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.REQUEST_INSTALL_PACKAGES",
                "android.permission.QUERY_ALL_PACKAGES",
                "android.permission.INTERNET"
            ]));

        Assert.Equal(AppPermissionRiskLevel.Dangerous, result.Level);
        Assert.Contains("SU-APK-INSTALL-01", result.MatchedRuleIds);
    }

    [Fact]
    public void Analyze_does_not_apply_android_17_local_network_rule_to_lower_targets()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.ACCESS_LOCAL_NETWORK",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 37,
            TargetSdkVersion: 36));

        Assert.Equal(AppPermissionRiskLevel.Safe, result.Level);
    }

    [Fact]
    public void Analyze_flags_android_17_health_permissions_by_prefix()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.health.READ_SYMPTOM_FEVER",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 37,
            TargetSdkVersion: 37));

        Assert.Equal(AppPermissionRiskLevel.Dangerous, result.Level);
    }
}
