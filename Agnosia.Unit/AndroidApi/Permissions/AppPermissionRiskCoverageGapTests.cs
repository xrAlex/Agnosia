using Agnosia.Android.Permissions;
using Agnosia.Models;
using Agnosia.ViewModels;
using Xunit;
using static Agnosia.Unit.ViewModels.AppRiskReportBuilderTests;

namespace Agnosia.Unit.AndroidApi.Permissions;

public sealed class AppPermissionRiskCoverageGapTests
{
    private static string Permission(string name) => "android.permission." + name;

    [Theory]
    [InlineData("com.android.voicemail.permission.ADD_VOICEMAIL", "добавлять сообщения в голосовую почту")]
    [InlineData("android.permission.ACCEPT_HANDOVER", "продолжать звонки")]
    public void Additional_call_capabilities_require_effective_grants(string permission, string words)
    {
        var input = new AppPermissionRiskInput([permission], DeviceSdkVersion: 36, TargetSdkVersion: 36,
            GrantedPermissions: [permission]);
        var analysis = AppPermissionRiskCatalog.Analyze(input);
        Assert.NotEmpty(analysis.Findings);
        Assert.Contains(words, Body(Build(analysis.Findings.ToArray())));
        Assert.Contains(permission, analysis.RuntimePermissions);
        Assert.Empty(AppPermissionRiskCatalog.Analyze(input with { GrantedPermissions = [] }).Findings);
        Assert.Empty(AppPermissionRiskCatalog.Analyze(input with { DeniedPermissions = [permission] }).Findings);
        Assert.Empty(AppPermissionRiskCatalog.Analyze(input with { BlockedAppOpPermissions = [permission] }).Findings);
    }

    [Theory]
    [InlineData("GET_ACCOUNTS", "аккаунтах")]
    [InlineData("READ_PHONE_STATE", "идёт ли звонок")]
    [InlineData("ANSWER_PHONE_CALLS", "отвечать")]
    [InlineData("BLUETOOTH_SCAN", "обнаруживать")]
    [InlineData("BLUETOOTH_CONNECT", "подключёнными")]
    [InlineData("NEARBY_WIFI_DEVICES", "Wi-Fi")]
    public void Standalone_access_is_reported_only_with_effective_grant(string name, string words)
    {
        var permission = Permission(name);
        var input = new AppPermissionRiskInput([permission], DeviceSdkVersion: 36,
            TargetSdkVersion: 36, GrantedPermissions: [permission]);
        var analysis = AppPermissionRiskCatalog.Analyze(input);
        Assert.NotEmpty(analysis.Findings);
        Assert.Contains(words, Body(Build(analysis.Findings.ToArray())));
        Assert.Empty(AppPermissionRiskCatalog.Analyze(input with { GrantedPermissions = [] }).Findings);
        Assert.Empty(AppPermissionRiskCatalog.Analyze(input with { DeniedPermissions = [permission] }).Findings);
        Assert.Empty(AppPermissionRiskCatalog.Analyze(input with { BlockedAppOpPermissions = [permission] }).Findings);
    }

    [Fact]
    public void Package_install_access_does_not_require_package_inventory()
    {
        var input = new AppPermissionRiskInput([Permission("REQUEST_INSTALL_PACKAGES")],
            CanRequestPackageInstalls: true);
        var analysis = AppPermissionRiskCatalog.Analyze(input);
        Assert.Contains("предлагать установку", Body(Build(analysis.Findings.ToArray())));
        Assert.Empty(AppPermissionRiskCatalog.Analyze(input with { CanRequestPackageInstalls = false }).Findings);
        Assert.Empty(AppPermissionRiskCatalog.Analyze(input with { CanRequestPackageInstalls = null }).Findings);
    }

    [Theory]
    [InlineData("BODY_SENSORS", "BODY_SENSORS_BACKGROUND", 33, "показания датчиков")]
    [InlineData("health.READ_HEART_RATE", "health.READ_HEALTH_DATA_IN_BACKGROUND", 36, "здоровье")]
    public void Background_health_access_is_reported_offline(string data, string background, int sdk, string words)
    {
        var permissions = new[] { Permission(data), Permission(background) };
        var analysis = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(permissions,
            DeviceSdkVersion: sdk, TargetSdkVersion: sdk, GrantedPermissions: permissions));
        Assert.Equal(AppPermissionRiskLevel.Dangerous, analysis.Level);
        var body = Body(Build(analysis.Findings.ToArray()));
        Assert.Contains(words, body);
        Assert.Contains("не открыто", body);
        Assert.DoesNotContain("через интернет", body);
    }

    [Theory]
    [InlineData("BODY_SENSORS", "BODY_SENSORS_BACKGROUND", 33)]
    [InlineData("health.READ_HEART_RATE", "health.READ_HEALTH_DATA_IN_BACKGROUND", 36)]
    public void Foreground_only_health_data_cannot_produce_critical_background_finding(string data, string background, int sdk)
    {
        var permissions = new[] { Permission(data), Permission(background), Permission("INTERNET") };
        var analysis = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(permissions,
            DeviceSdkVersion: sdk, TargetSdkVersion: sdk, GrantedPermissions: permissions,
            ForegroundOnlyPermissions: [Permission(data)]));
        Assert.NotEqual(AppPermissionRiskLevel.Critical, analysis.Level);
        Assert.DoesNotContain(analysis.Findings, f => f.RuleLevel == AppPermissionRiskLevel.Critical);
        Assert.DoesNotContain(analysis.RuntimePermissions, p => p == Permission(background));
    }

    [Theory]
    [InlineData("BODY_SENSORS", "BODY_SENSORS_BACKGROUND", 33)]
    [InlineData("health.READ_HEART_RATE", "health.READ_HEALTH_DATA_IN_BACKGROUND", 36)]
    public void Background_modifier_requires_effective_underlying_data(string data, string background, int sdk)
    {
        var permissions = new[] { Permission(data), Permission(background), Permission("INTERNET") };
        var analysis = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(permissions,
            DeviceSdkVersion: sdk, TargetSdkVersion: sdk, GrantedPermissions: [Permission(background)],
            DeniedPermissions: [Permission(data)]));
        Assert.Empty(analysis.Findings);
        Assert.DoesNotContain(Permission(background), analysis.RuntimePermissions);
    }

    [Fact]
    public void Bluetooth_combination_does_not_count_standalone_access_twice()
    {
        var permissions = new[] { Permission("BLUETOOTH_SCAN"), Permission("BLUETOOTH_CONNECT") };
        var analysis = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(permissions, GrantedPermissions: permissions));
        Assert.Equal(3, analysis.ScoreBreakdown.DataSensitivityScore);
        Assert.Equal(4, analysis.Score);
    }

    [Fact]
    public void Mms_is_not_described_as_sms()
    {
        var text = Body(Build(Finding("SU-SMS-MMS-01", "RECEIVE_MMS")));
        Assert.Contains("MMS", text);
        Assert.DoesNotContain("SMS", text);
    }
}
