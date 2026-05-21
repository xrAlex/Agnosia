using Agnosia.Models;
using Xunit;

namespace Agnosia.Unit.Models;

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
    public void Classify_returns_dangerous_for_suspicious_location_network_combination()
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
    public void Analyze_uses_read_external_storage_only_for_android_12_family()
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
    public void Analyze_uses_granular_media_permissions_for_android_13_plus()
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
    public void Analyze_uses_service_permission_control_surfaces()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [
                "android.permission.INTERNET",
                "android.permission.SYSTEM_ALERT_WINDOW"
            ],
            ServicePermissions: ["android.permission.BIND_ACCESSIBILITY_SERVICE"]));

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
    public void Analyze_downgrades_legacy_write_external_storage_by_target_sdk()
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
