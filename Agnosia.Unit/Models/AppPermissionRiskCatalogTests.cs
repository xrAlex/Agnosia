using Agnosia.Models;
using Xunit;

namespace Agnosia.Unit.Models;

public sealed class AppPermissionRiskCatalogTests
{
    [Fact]
    public void Classify_returns_safe_when_no_tracked_permissions_are_requested()
    {
        var result = AppPermissionRiskCatalog.Classify([
            "android.permission.VIBRATE",
            "android.permission.SET_ALARM"
        ]);

        Assert.Equal(AppPermissionRiskLevel.Safe, result);
    }

    [Fact]
    public void Classify_returns_dangerous_when_dangerous_permission_is_requested()
    {
        var result = AppPermissionRiskCatalog.Classify([
            "android.permission.INTERNET"
        ]);

        Assert.Equal(AppPermissionRiskLevel.Dangerous, result);
    }

    [Fact]
    public void Classify_returns_critical_when_critical_permission_is_requested()
    {
        var result = AppPermissionRiskCatalog.Classify([
            "android.permission.ACCESS_COARSE_LOCATION",
            "android.permission.CAMERA"
        ]);

        Assert.Equal(AppPermissionRiskLevel.Critical, result);
    }

    [Fact]
    public void Analyze_returns_matched_permissions_without_duplicates()
    {
        var result = AppPermissionRiskCatalog.Analyze([
            "android.permission.VIBRATE",
            "android.permission.INTERNET",
            "android.permission.CAMERA",
            "android.permission.INTERNET"
        ]);

        Assert.Equal(AppPermissionRiskLevel.Critical, result.Level);
        Assert.Equal(
            [
                "android.permission.INTERNET",
                "android.permission.CAMERA"
            ],
            result.RiskyPermissions);
    }
}
