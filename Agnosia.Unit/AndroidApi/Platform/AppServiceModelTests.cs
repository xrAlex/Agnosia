using System.Text.Json;
using Agnosia.Android.Api.Platform;
using Agnosia.Models;
using Xunit;

namespace Agnosia.Unit.AndroidApi.Platform;

public sealed class AppServiceModelTests
{
    // Проверяет JSON contract DTO для передачи app inventory между Android профилями.
    [Fact]
    public void App_inventory_item_round_trips_through_default_json_serializer()
    {
        var model = FullInventoryModel();

        var json = JsonSerializer.Serialize(new[] { model });
        var roundTrip = JsonSerializer.Deserialize<AppServiceModel[]>(json);

        var result = Assert.Single(roundTrip ?? []);
        AssertEquivalent(model, result);
    }

    // Проверяет компактный DTO для системных приложений в work-list inventory.
    [Fact]
    public void App_inventory_item_round_trips_without_system_apk_paths()
    {
        var model = new AppServiceModel
        {
            PackageName = "org.example.system",
            Label = "System Example",
            SourceDirectory = null,
            SplitApks = [],
            IconPng = null,
            IsSystem = true,
            IsHidden = false,
            CanLaunch = true,
            IsInstalled = true
        };

        var json = JsonSerializer.Serialize(new[] { model });
        var roundTrip = JsonSerializer.Deserialize<AppServiceModel[]>(json);

        var result = Assert.Single(roundTrip ?? []);
        Assert.Null(result.SourceDirectory);
        Assert.Empty(result.SplitApks);
        AssertEquivalent(model, result);
    }

    private static AppServiceModel FullInventoryModel()
    {
        return new AppServiceModel
        {
            PackageName = "org.example.app",
            Label = "Example",
            SourceDirectory = "/data/app/org.example.app/base.apk",
            SplitApks = ["/data/app/org.example.app/config.arm64.apk"],
            IconPng = [1, 2, 3],
            IsSystem = true,
            IsHidden = true,
            CanLaunch = true,
            IsInstalled = true,
            PermissionRiskLevel = AppPermissionRiskLevel.Dangerous,
            RiskyPermissions = ["android.permission.CAMERA"]
        };
    }

    private static void AssertEquivalent(AppServiceModel expected, AppServiceModel actual)
    {
        Assert.Equal(expected.PackageName, actual.PackageName);
        Assert.Equal(expected.Label, actual.Label);
        Assert.Equal(expected.SourceDirectory, actual.SourceDirectory);
        Assert.Equal(expected.SplitApks, actual.SplitApks);
        Assert.Equal(expected.IconPng, actual.IconPng);
        Assert.Equal(expected.IsSystem, actual.IsSystem);
        Assert.Equal(expected.IsHidden, actual.IsHidden);
        Assert.Equal(expected.CanLaunch, actual.CanLaunch);
        Assert.Equal(expected.IsInstalled, actual.IsInstalled);
        Assert.Equal(expected.PermissionRiskLevel, actual.PermissionRiskLevel);
        Assert.Equal(expected.RiskyPermissions, actual.RiskyPermissions);
    }
}
