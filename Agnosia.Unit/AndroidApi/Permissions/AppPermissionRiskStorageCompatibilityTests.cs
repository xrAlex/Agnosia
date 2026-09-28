using Agnosia.Android.Permissions;
using Agnosia.Models;
using Xunit;
using static Agnosia.Unit.ViewModels.AppRiskReportBuilderTests;

namespace Agnosia.Unit.AndroidApi.Permissions;

public sealed class AppPermissionRiskStorageCompatibilityTests
{
    private const string ReadStorage = "android.permission.READ_EXTERNAL_STORAGE";
    private const string WriteStorage = "android.permission.WRITE_EXTERNAL_STORAGE";
    private const string MediaLocation = "android.permission.ACCESS_MEDIA_LOCATION";
    private const string Internet = "android.permission.INTERNET";

    [Theory]
    [InlineData(32, 32, true)]
    [InlineData(32, 35, true)]
    [InlineData(33, 32, true)]
    [InlineData(34, 32, true)]
    [InlineData(36, 29, true)]
    [InlineData(36, 33, false)]
    [InlineData(36, 0, false)]
    public void Legacy_media_read_depends_on_both_device_and_target(int sdk, int target, bool effective)
    {
        var input = new AppPermissionRiskInput([ReadStorage, MediaLocation, Internet],
            DeviceSdkVersion: sdk, TargetSdkVersion: target, GrantedPermissions: [ReadStorage, MediaLocation]);
        var analysis = AppPermissionRiskCatalog.Analyze(input);
        Assert.Equal(effective, analysis.MatchedRuleIds.Contains("SU-MEDIA-LEGACY-01"));
        Assert.Equal(effective, analysis.MatchedRuleIds.Contains("SU-MEDIA-LOC-LEGACY-01"));
        Assert.Equal(effective, analysis.RuntimePermissions.Contains(ReadStorage));
        Assert.Empty(AppPermissionRiskCatalog.Analyze(input with { DeniedPermissions = [ReadStorage] }).Findings);
        Assert.Empty(AppPermissionRiskCatalog.Analyze(input with { BlockedAppOpPermissions = [ReadStorage] }).Findings);
    }

    [Fact]
    public void Legacy_read_explains_shared_media_instead_of_all_phone_files()
    {
        var analysis = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput([ReadStorage],
            DeviceSdkVersion: 32, TargetSdkVersion: 32, GrantedPermissions: [ReadStorage]));
        var text = Body(Build(analysis.Findings.ToArray()));
        Assert.Contains("фотографии", text);
        Assert.Contains("видео", text);
        Assert.Contains("аудиофайлы", text);
        Assert.DoesNotContain("читать файлы на телефоне", text);
    }

    [Fact]
    public void Write_permission_alone_does_not_confirm_legacy_storage_mode()
    {
        var analysis = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput([WriteStorage, Internet],
            DeviceSdkVersion: 32, TargetSdkVersion: 29, GrantedPermissions: [WriteStorage]));
        Assert.Empty(analysis.Findings);
        Assert.DoesNotContain(WriteStorage, analysis.RuntimePermissions);
        Assert.Contains(WriteStorage, analysis.UnavailableChecks);
    }

    [Theory]
    [InlineData(32, 29, true, true)]
    [InlineData(36, 29, true, true)]
    [InlineData(36, 29, false, false)]
    [InlineData(36, 29, null, false)]
    [InlineData(36, 30, true, false)]
    public void Legacy_write_and_its_channel_require_confirmed_storage_mode(int sdk, int target, bool? legacy, bool effective)
    {
        var analysis = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(
            [WriteStorage, "android.permission.READ_CONTACTS"], DeviceSdkVersion: sdk, TargetSdkVersion: target,
            GrantedPermissions: [WriteStorage, "android.permission.READ_CONTACTS"], HasLegacyExternalStorageAccess: legacy));
        Assert.Equal(effective, analysis.MatchedRuleIds.Contains("SU-FILE-WRITE-LEGACY-01"));
        Assert.Equal(effective ? 1 : 0, analysis.ScoreBreakdown.ExfiltrationScore);
    }

    [Fact]
    public void Legacy_media_location_does_not_double_count_the_media_access()
    {
        var analysis = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput([ReadStorage, MediaLocation],
            DeviceSdkVersion: 34, TargetSdkVersion: 32, GrantedPermissions: [ReadStorage, MediaLocation]));
        Assert.Equal(4, analysis.ScoreBreakdown.DataSensitivityScore);
    }
}
