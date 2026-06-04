using Agnosia.Android.Permissions;
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
    public void Analyze_keeps_manifest_only_runtime_sensitive_permissions_safe()
    {
        var cameraResult = AppPermissionRiskCatalog.Analyze([
            "android.permission.CAMERA",
            "android.permission.INTERNET"
        ]);
        var microphoneResult = AppPermissionRiskCatalog.Analyze([
            "android.permission.RECORD_AUDIO",
            "android.permission.INTERNET"
        ]);
        var contactsResult = AppPermissionRiskCatalog.Analyze([
            "android.permission.READ_CONTACTS",
            "android.permission.INTERNET"
        ]);

        Assert.Equal(AppPermissionRiskLevel.Safe, cameraResult.Level);
        Assert.Equal(AppPermissionRiskLevel.Safe, microphoneResult.Level);
        Assert.Equal(AppPermissionRiskLevel.Safe, contactsResult.Level);
    }

    [Fact]
    public void Analyze_flags_granted_runtime_sensitive_permission_without_network()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            ["android.permission.CAMERA"],
            GrantedPermissions: ["android.permission.CAMERA"]));

        Assert.Equal(AppPermissionRiskLevel.Dangerous, result.Level);
        Assert.Equal(["SU-CAM-01"], result.MatchedRuleIds);
    }

    [Fact]
    public void Analyze_treats_allowed_appop_as_confirmed_runtime_access()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            ["android.permission.RECORD_AUDIO"],
            IsMicrophoneAppOpAllowed: true));

        Assert.Equal(AppPermissionRiskLevel.Dangerous, result.Level);
        Assert.Equal(["SU-MIC-01"], result.MatchedRuleIds);
    }

    [Fact]
    public void Classify_returns_safe_for_manifest_only_location_network_combination()
    {
        var result = AppPermissionRiskCatalog.Classify([
            "android.permission.ACCESS_FINE_LOCATION",
            "android.permission.INTERNET"
        ]);

        Assert.Equal(AppPermissionRiskLevel.Safe, result);
    }

    [Fact]
    public void Analyze_returns_critical_for_granted_background_location_network_combination()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.ACCESS_FINE_LOCATION",
                "android.permission.ACCESS_BACKGROUND_LOCATION",
                "android.permission.INTERNET"
            ],
            GrantedPermissions:
            [
                "android.permission.ACCESS_FINE_LOCATION",
                "android.permission.ACCESS_BACKGROUND_LOCATION"
            ]));

        Assert.Equal(AppPermissionRiskLevel.Critical, result.Level);
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
            ForegroundServiceTypes: ["camera"],
            GrantedPermissions: ["android.permission.CAMERA"]));

        Assert.Equal(AppPermissionRiskLevel.Critical, result.Level);
        Assert.Equal(
            [
                "android.permission.INTERNET",
                "android.permission.CAMERA"
            ],
            result.RiskyPermissions);
    }

    [Fact]
    public void Analyze_flags_granted_legacy_read_external_storage_on_supported_sdks()
    {
        var manifestOnlyResult = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.READ_EXTERNAL_STORAGE",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 32));
        var android12Result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.READ_EXTERNAL_STORAGE",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 32,
            GrantedPermissions: ["android.permission.READ_EXTERNAL_STORAGE"]));
        var android13Result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.READ_EXTERNAL_STORAGE",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 33));

        Assert.Equal(AppPermissionRiskLevel.Safe, manifestOnlyResult.Level);
        Assert.Equal(AppPermissionRiskLevel.Dangerous, android12Result.Level);
        Assert.Equal(AppPermissionRiskLevel.Safe, android13Result.Level);
    }

    [Fact]
    public void Analyze_keeps_manifest_only_granular_media_permissions_safe()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.READ_MEDIA_IMAGES",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 33));

        Assert.Equal(AppPermissionRiskLevel.Safe, result.Level);
    }

    [Fact]
    public void Analyze_flags_granted_granular_media_permissions_as_dangerous()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.READ_MEDIA_IMAGES",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 33,
            GrantedPermissions: ["android.permission.READ_MEDIA_IMAGES"]));

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
        Assert.Equal(13, result.Score);
        Assert.Equal(13, result.RawScore);
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
    public void Analyze_keeps_media_projection_declaration_safe_without_observed_capture()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.FOREGROUND_SERVICE_MEDIA_PROJECTION",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 34,
            ForegroundServiceTypes: ["mediaProjection"]));

        Assert.Equal(AppPermissionRiskLevel.Safe, result.Level);
        Assert.DoesNotContain("SU-SCR-FGS-01", result.MatchedRuleIds);
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
            IsMediaProjectionActive: true));

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
            ForegroundServiceTypes: ["microphone"],
            GrantedPermissions: ["android.permission.RECORD_AUDIO"]));

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
            DeviceSdkVersion: 34,
            GrantedPermissions: ["android.permission.RECORD_AUDIO"]));

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
            ],
            GrantedPermissions:
            [
                "android.permission.ACCESS_FINE_LOCATION",
                "android.permission.ACCESS_COARSE_LOCATION",
                "android.permission.ACCESS_BACKGROUND_LOCATION"
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
        Assert.Equal(8, result.Score);
        Assert.Equal(15, result.RawScore);
        Assert.Equal(AppPermissionRiskConfidence.High, result.Confidence);
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
        Assert.Equal(7, result.Score);
        Assert.Equal(13, result.RawScore);
    }

    [Fact]
    public void Analyze_scores_fine_location_above_coarse_location()
    {
        var fineResult = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.ACCESS_FINE_LOCATION",
                "android.permission.INTERNET"
            ],
            GrantedPermissions: ["android.permission.ACCESS_FINE_LOCATION"]));
        var coarseResult = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.ACCESS_COARSE_LOCATION",
                "android.permission.INTERNET"
            ],
            GrantedPermissions: ["android.permission.ACCESS_COARSE_LOCATION"]));

        Assert.Equal(AppPermissionRiskLevel.Dangerous, fineResult.Level);
        Assert.Equal(AppPermissionRiskLevel.Dangerous, coarseResult.Level);
        Assert.True(fineResult.RawScore > coarseResult.RawScore);
    }

    [Fact]
    public void Analyze_treats_unknown_runtime_grant_state_as_unconfirmed()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.RECORD_AUDIO",
                "android.permission.FOREGROUND_SERVICE_MICROPHONE",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 34,
            GrantedPermissions: ["android.permission.CAMERA"]));

        Assert.Equal(AppPermissionRiskLevel.Safe, result.Level);
        Assert.Empty(result.MatchedRuleIds);
        Assert.Equal(AppPermissionRiskConfidence.None, result.Confidence);
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
    public void Analyze_reports_only_granted_runtime_permissions_when_grant_state_is_known()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.RECORD_AUDIO",
                "android.permission.CAMERA",
                "android.permission.INTERNET"
            ],
            GrantedPermissions: ["android.permission.CAMERA"],
            DeniedPermissions: ["android.permission.RECORD_AUDIO"]));

        Assert.Equal(["android.permission.CAMERA"], result.RuntimePermissions);
    }

    [Fact]
    public void Analyze_keeps_declared_runtime_permissions_when_grant_state_is_unknown()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.RECORD_AUDIO",
                "android.permission.CAMERA",
                "android.permission.INTERNET"
            ]));

        Assert.Equal(
            [
                "android.permission.RECORD_AUDIO",
                "android.permission.CAMERA"
            ],
            result.RuntimePermissions);
    }

    [Fact]
    public void Analyze_flags_granted_legacy_write_external_storage_on_legacy_targets()
    {
        var legacyTargetResult = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.WRITE_EXTERNAL_STORAGE",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 32,
            TargetSdkVersion: 29,
            GrantedPermissions: ["android.permission.WRITE_EXTERNAL_STORAGE"]));
        var modernTargetResult = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.WRITE_EXTERNAL_STORAGE",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 32,
            TargetSdkVersion: 30,
            GrantedPermissions: ["android.permission.WRITE_EXTERNAL_STORAGE"]));

        Assert.Equal(AppPermissionRiskLevel.Dangerous, legacyTargetResult.Level);
        Assert.Equal(AppPermissionRiskLevel.Safe, modernTargetResult.Level);
    }

    [Fact]
    public void Analyze_keeps_health_permissions_safe_without_health_rules()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.health.READ_HEART_RATE",
                "android.permission.health.READ_HEALTH_DATA_IN_BACKGROUND",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 36,
            TargetSdkVersion: 36));

        Assert.Equal(AppPermissionRiskLevel.Safe, result.Level);
        Assert.Empty(result.MatchedRuleIds);
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
            TargetSdkVersion: 36,
            GrantedPermissions: ["android.permission.NEARBY_WIFI_DEVICES"]));

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
            TargetSdkVersion: 37,
            GrantedPermissions: ["android.permission.ACCESS_LOCAL_NETWORK"]));

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
            TargetSdkVersion: 37,
            GrantedPermissions: ["android.permission.ACCESS_LOCAL_NETWORK"]));

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
    public void Analyze_flags_vpn_only_when_control_is_enabled()
    {
        var declaredResult = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [],
            ServicePermissions: ["android.permission.BIND_VPN_SERVICE"]));
        var enabledResult = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [],
            IsVpnControlEnabled: true));

        Assert.Equal(AppPermissionRiskLevel.Safe, declaredResult.Level);
        Assert.Equal(AppPermissionRiskLevel.Dangerous, enabledResult.Level);
        Assert.Equal(["SU-VPN-01"], enabledResult.MatchedRuleIds);
        Assert.Equal(["android.permission.BIND_VPN_SERVICE"], enabledResult.RiskyPermissions);
    }

    [Fact]
    public void Analyze_flags_assistant_screen_content_only_when_role_is_enabled()
    {
        var declaredResult = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.READ_ASSIST_STRUCTURE_SCREEN_CONTENT"
            ],
            DeviceSdkVersion: 37));
        var enabledResult = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [],
            DeviceSdkVersion: 37,
            IsAssistantScreenContentEnabled: true));

        Assert.Equal(AppPermissionRiskLevel.Safe, declaredResult.Level);
        Assert.Equal(AppPermissionRiskLevel.Dangerous, enabledResult.Level);
        Assert.Equal(["SU-ASSIST-SCREEN-01"], enabledResult.MatchedRuleIds);
        Assert.Equal(["android.permission.READ_ASSIST_STRUCTURE_SCREEN_CONTENT"], enabledResult.RiskyPermissions);
    }

    [Fact]
    public void Analyze_reports_camera_boot_persistence_as_critical_when_camera_is_confirmed()
    {
        var android14TargetResult = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.CAMERA",
                "android.permission.RECEIVE_BOOT_COMPLETED",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 34,
            TargetSdkVersion: 34,
            ForegroundServiceTypes: ["camera"],
            GrantedPermissions: ["android.permission.CAMERA"]));
        var android15TargetResult = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.CAMERA",
                "android.permission.RECEIVE_BOOT_COMPLETED",
                "android.permission.INTERNET"
            ],
            DeviceSdkVersion: 35,
            TargetSdkVersion: 35,
            ForegroundServiceTypes: ["camera"],
            GrantedPermissions: ["android.permission.CAMERA"]));

        Assert.Equal(AppPermissionRiskLevel.Critical, android14TargetResult.Level);
        Assert.Contains("CR-CAM-PERSIST-01", android14TargetResult.MatchedRuleIds);
        Assert.Equal(AppPermissionRiskLevel.Critical, android15TargetResult.Level);
        Assert.DoesNotContain("CR-CAM-PERSIST-01", android15TargetResult.MatchedRuleIds);
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
            TargetSdkVersion: 36,
            GrantedPermissions: ["android.permission.ACCESS_LOCAL_NETWORK"]));

        Assert.Equal(AppPermissionRiskLevel.Safe, result.Level);
    }

}
