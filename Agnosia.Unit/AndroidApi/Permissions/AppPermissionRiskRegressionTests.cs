using Agnosia.Android.Permissions;
using Agnosia.Models;
using Xunit;

namespace Agnosia.Unit.AndroidApi.Permissions;

public sealed class AppPermissionRiskRegressionTests
{
    private const string Internet = "android.permission.INTERNET";

    [Theory]
    [InlineData("READ_CONTACTS")]
    [InlineData("READ_SMS")]
    [InlineData("health.READ_HEART_RATE")]
    public void Appops_denial_applies_to_all_mapped_runtime_capabilities(string name)
    {
        var permission = Permission(name);
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [permission, Internet], DeviceSdkVersion: 36, GrantedPermissions: [permission],
            BlockedAppOpPermissions: [permission]));
        Assert.Empty(result.Findings);
        Assert.Empty(result.RuntimePermissions);
    }

    [Fact]
    public void Denied_health_types_are_not_reported_as_effective_risky_permissions()
    {
        var result = Analyze(["health.READ_HEART_RATE", "health.READ_BLOOD_PRESSURE"],
            ["health.READ_HEART_RATE"], ["health.READ_BLOOD_PRESSURE"]);
        Assert.DoesNotContain(Permission("health.READ_BLOOD_PRESSURE"), result.RiskyPermissions);
        Assert.DoesNotContain(result.Findings.SelectMany(f => f.Evidence), e => e.SignalId == Permission("health.READ_BLOOD_PRESSURE"));
    }

    [Theory]
    [InlineData("android.observed.DefaultSmsRole")]
    [InlineData("android.observed.DefaultDialerRole")]
    [InlineData("android.observed.AssistantRole")]
    public void Selected_roles_are_context_not_proof_of_screen_or_message_access(string role)
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput([Internet], ObservedSignals: [role]));
        Assert.Equal(AppPermissionRiskLevel.Safe, result.Level);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(AppPermissionRiskEvidenceState.Enabled, finding.Evidence.Single(e => e.SignalId == role).State);
    }

    [Fact]
    public void Explicitly_denied_package_inventory_does_not_match()
    {
        var result = Analyze(["QUERY_ALL_PACKAGES"], [], ["QUERY_ALL_PACKAGES"]);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Media_location_enhancement_does_not_count_the_same_photos_twice()
    {
        var result = Analyze(["READ_MEDIA_IMAGES", "ACCESS_MEDIA_LOCATION"], ["READ_MEDIA_IMAGES", "ACCESS_MEDIA_LOCATION"]);
        Assert.Equal(4, result.ScoreBreakdown.DataSensitivityScore);
        Assert.Equal(2, result.Findings.Count);
    }

    [Fact]
    public void Granted_background_location_is_detected_without_internet()
    {
        var permissions = new[] { Permission("ACCESS_FINE_LOCATION"), Permission("ACCESS_BACKGROUND_LOCATION") };
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(permissions, GrantedPermissions: permissions));
        Assert.NotEmpty(result.Findings);
        Assert.Equal(AppPermissionRiskLevel.Dangerous, result.Level);
    }

    [Fact]
    public void Foreground_appop_prevents_background_critical_claim()
    {
        var permissions = new[] { Permission("ACCESS_FINE_LOCATION"), Permission("ACCESS_BACKGROUND_LOCATION"), Internet };
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(permissions, GrantedPermissions: permissions,
            ForegroundOnlyPermissions: [Permission("ACCESS_FINE_LOCATION")]));
        Assert.DoesNotContain(result.Findings, finding => finding.RuleLevel == AppPermissionRiskLevel.Critical);
        Assert.Contains(result.Findings.SelectMany(f => f.Evidence), e => e.State == AppPermissionRiskEvidenceState.ForegroundOnly);
    }

    [Fact]
    public void Allowed_appop_without_runtime_grant_does_not_confirm_access()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [Permission("CAMERA")], IsCameraAppOpAllowed: true));
        Assert.Empty(result.Findings);
        Assert.Contains("UnavailableChecks", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Fact]
    public void Unknown_special_access_survives_analysis_as_incomplete_check()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput([Permission("SYSTEM_ALERT_WINDOW")]));
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("\"UnavailableChecks\":[\"android.permission.SYSTEM_ALERT_WINDOW\"]", json);
    }

    [Theory]
    [InlineData("READ_CALENDAR")]
    [InlineData("WRITE_CALENDAR")]
    [InlineData("WRITE_CONTACTS")]
    [InlineData("READ_SMS")]
    [InlineData("RECEIVE_SMS")]
    [InlineData("SEND_SMS")]
    [InlineData("RECEIVE_MMS")]
    [InlineData("RECEIVE_WAP_PUSH")]
    [InlineData("READ_CALL_LOG")]
    [InlineData("WRITE_CALL_LOG")]
    [InlineData("CALL_PHONE")]
    [InlineData("BODY_SENSORS")]
    [InlineData("ACTIVITY_RECOGNITION")]
    [InlineData("UWB_RANGING")]
    [InlineData("BLUETOOTH_ADVERTISE")]
    [InlineData("health.READ_HEART_RATE")]
    [InlineData("health.WRITE_STEPS")]
    [InlineData("health.READ_MEDICAL_DATA_VACCINES")]
    public void Sensitive_capabilities_have_findings_only_when_granted(string permission)
    {
        var input = new AppPermissionRiskInput([Permission(permission)], DeviceSdkVersion: 36, TargetSdkVersion: 35,
            GrantedPermissions: [Permission(permission)]);
        Assert.NotEmpty(AppPermissionRiskCatalog.Analyze(input).Findings);
        Assert.Empty(AppPermissionRiskCatalog.Analyze(input with { GrantedPermissions = [], DeniedPermissions = [Permission(permission)] }).Findings);
    }

    [Fact]
    public void Background_health_modifier_alone_is_not_health_data_access()
    {
        var result = Analyze(["health.READ_HEALTH_DATA_IN_BACKGROUND"], ["health.READ_HEALTH_DATA_IN_BACKGROUND"]);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Granted_background_health_data_has_critical_finding()
    {
        var result = Analyze(["health.READ_HEART_RATE", "health.READ_HEALTH_DATA_IN_BACKGROUND"],
            ["health.READ_HEART_RATE", "health.READ_HEALTH_DATA_IN_BACKGROUND"]);
        Assert.Contains(result.Findings, finding => finding.RuleId == "CR-HEALTH-BG-01");
    }

    [Fact]
    public void Health_permissions_do_not_apply_before_android_14()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [Permission("health.READ_HEART_RATE"), Internet], DeviceSdkVersion: 33,
            GrantedPermissions: [Permission("health.READ_HEART_RATE")]));
        Assert.Empty(result.Findings);
        Assert.Contains(Permission("health.READ_HEART_RATE"), result.UnavailableChecks);
    }

    [Fact]
    public void Overlapping_camera_rules_are_scored_once()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [Permission("CAMERA"), Permission("RECEIVE_BOOT_COMPLETED"), Internet],
            DeviceSdkVersion: 33, TargetSdkVersion: 33, ForegroundServiceTypes: ["camera"],
            GrantedPermissions: [Permission("CAMERA")]));
        Assert.Equal(4, result.ScoreBreakdown.DataSensitivityScore);
        Assert.True(result.RawScore > result.Score);
    }

    [Fact]
    public void Denied_background_location_does_not_hide_foreground_location()
    {
        var result = Analyze(["ACCESS_FINE_LOCATION", "ACCESS_BACKGROUND_LOCATION"], ["ACCESS_FINE_LOCATION"], ["ACCESS_BACKGROUND_LOCATION"]);
        Assert.Contains("SU-LOC-01", result.MatchedRuleIds);
        Assert.Equal(AppPermissionRiskLevel.Dangerous, result.Level);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Full_photos_take_precedence_over_selected_photos(bool selectedGranted)
    {
        var granted = selectedGranted ? new[] { "READ_MEDIA_IMAGES", "READ_MEDIA_VISUAL_USER_SELECTED" } : ["READ_MEDIA_IMAGES"];
        var result = Analyze(["READ_MEDIA_IMAGES", "READ_MEDIA_VISUAL_USER_SELECTED"], granted, selectedGranted ? [] : ["READ_MEDIA_VISUAL_USER_SELECTED"]);
        Assert.Contains("SU-MEDIA-IMG-01", result.MatchedRuleIds);
        Assert.DoesNotContain("SU-MEDIA-PARTIAL-01", result.MatchedRuleIds);
    }

    [Fact]
    public void Denied_channels_do_not_increase_contacts_score()
    {
        var baseline = Analyze(["READ_CONTACTS"], ["READ_CONTACTS"]);
        var denied = Analyze(["READ_CONTACTS", "BLUETOOTH_SCAN", "SEND_SMS", "WRITE_EXTERNAL_STORAGE"], ["READ_CONTACTS"], ["BLUETOOTH_SCAN", "SEND_SMS", "WRITE_EXTERNAL_STORAGE"]);
        Assert.Equal(baseline.Score, denied.Score);
        Assert.Equal(baseline.Level, denied.Level);
    }

    [Fact]
    public void Denied_runtime_grant_wins_over_allowed_appop()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            ["android.permission.CAMERA", Internet], DeniedPermissions: ["android.permission.CAMERA"], IsCameraAppOpAllowed: true));
        Assert.DoesNotContain("SU-CAM-01", result.MatchedRuleIds);
    }

    [Fact]
    public void Unrelated_foreground_service_does_not_raise_sensor_score()
    {
        var input = new AppPermissionRiskInput(["android.permission.CAMERA", "android.permission.RECORD_AUDIO"],
            GrantedPermissions: ["android.permission.CAMERA", "android.permission.RECORD_AUDIO"]);
        var baseline = AppPermissionRiskCatalog.Analyze(input);
        var unrelated = AppPermissionRiskCatalog.Analyze(input with { ForegroundServiceTypes = ["location"] });
        Assert.Equal(baseline.Score, unrelated.Score);
        Assert.Equal(AppPermissionRiskLevel.Dangerous, baseline.Level);
    }

    [Fact]
    public void Bluetooth_scan_is_not_a_data_transmission_channel()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            ["android.permission.FOREGROUND_SERVICE_MEDIA_PROJECTION", "android.permission.BLUETOOTH_SCAN"],
            DeviceSdkVersion: 34, ForegroundServiceTypes: ["mediaProjection"], IsMediaProjectionActive: true,
            GrantedPermissions: ["android.permission.BLUETOOTH_SCAN"]));
        Assert.DoesNotContain("CR-SCR-01", result.MatchedRuleIds);
        Assert.Equal(0, result.ScoreBreakdown.ExfiltrationScore);
    }

    [Fact]
    public void Findings_below_overall_threshold_are_retained()
    {
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            ["android.permission.READ_MEDIA_VISUAL_USER_SELECTED"], DeviceSdkVersion: 34,
            GrantedPermissions: ["android.permission.READ_MEDIA_VISUAL_USER_SELECTED"]));
        Assert.Single(result.Findings);
        Assert.Equal(1, result.Score);
        Assert.Equal(AppPermissionRiskLevel.Safe, result.Level);
    }

    private static AppPermissionRiskAnalysis Analyze(string[] requested, string[] granted, string[]? denied = null) =>
        AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            requested.Select(Permission).Append(Internet), DeviceSdkVersion: 36, TargetSdkVersion: 36,
            GrantedPermissions: granted.Select(Permission).Append(Internet), DeniedPermissions: denied?.Select(Permission)));

    private static string Permission(string name) => "android.permission." + name;
}
