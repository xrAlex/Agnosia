using Agnosia.Android.Permissions;
using Agnosia.Models;
using Xunit;
using static Agnosia.Unit.ViewModels.AppRiskReportBuilderTests;

namespace Agnosia.Unit.ViewModels;

public sealed class AppRiskReportPartialAccessTests
{
    private const string Heart = "android.permission.health.READ_HEART_RATE";
    private const string Steps = "android.permission.health.READ_STEPS";
    private const string Background = "android.permission.health.READ_HEALTH_DATA_IN_BACKGROUND";
    private const string History = "android.permission.health.READ_HEALTH_DATA_HISTORY";
    private const string Selected = "android.permission.READ_MEDIA_VISUAL_USER_SELECTED";
    private const string MediaLocation = "android.permission.ACCESS_MEDIA_LOCATION";

    [Fact]
    public void Selected_media_location_is_reported_without_full_library_access()
    {
        var input = new AppPermissionRiskInput([Selected, MediaLocation], DeviceSdkVersion: 34,
            TargetSdkVersion: 34, GrantedPermissions: [Selected, MediaLocation]);
        var analysis = AppPermissionRiskCatalog.Analyze(input);
        var text = Body(Build(analysis.Findings.ToArray()));
        Assert.Contains("выбранные вами", text);
        Assert.Contains("место съёмки", text);
        Assert.DoesNotContain("просматривать фотографии на телефоне", text);
        foreach (var denied in new[] { Selected, MediaLocation })
            Assert.DoesNotContain("место съёмки", Body(Build(AppPermissionRiskCatalog.Analyze(
                input with { DeniedPermissions = [denied] }).Findings.ToArray())));
    }

    [Fact]
    public void Mixed_health_grants_preserve_background_access_to_the_unrestricted_data()
    {
        var permissions = new[] { Heart, Steps, Background };
        var analysis = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(permissions,
            DeviceSdkVersion: 36, TargetSdkVersion: 36, GrantedPermissions: permissions,
            ForegroundOnlyPermissions: [Heart]));
        var text = Body(Build(analysis.Findings.ToArray()));
        Assert.Contains("во время использования", text);
        Assert.Contains("может читать часть записей о здоровье, когда приложение не открыто", text);
        Assert.DoesNotContain("запрашивает", text);
    }

    [Fact]
    public void Health_history_inherits_foreground_restriction_without_becoming_a_request()
    {
        var permissions = new[] { Heart, History };
        var analysis = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(permissions,
            DeviceSdkVersion: 36, TargetSdkVersion: 36, GrantedPermissions: permissions,
            ForegroundOnlyPermissions: [Heart]));
        var text = Body(Build(analysis.Findings.ToArray()));
        Assert.Contains("может читать историю записей о здоровье во время использования", text);
        Assert.DoesNotContain("запрашивает", text);
    }
}
