using Agnosia.Models;
using Xunit;

namespace Agnosia.Unit.Models;

public sealed class PermissionCatalogTests
{
    [Fact]
    public void Create_snapshot_uses_permission_catalog_text()
    {
        var snapshot = PermissionCatalog.CreateSnapshot(
            PermissionKind.VpnControl,
            isGranted: false,
            canRequest: true);

        Assert.Equal("Временное управление VPN", snapshot.Title);
        Assert.Equal("Основной профиль", snapshot.ProfileLabel);
        Assert.Equal("Позволяет приложению управлять VPN-соединениями", snapshot.Description);
        Assert.Equal("Получено", snapshot.GrantedLabel);
        Assert.Equal("Разрешить", snapshot.RequestLabel);
    }

    [Fact]
    public void Create_work_profile_snapshot_uses_catalog_action_for_setup_state()
    {
        var notSetUp = PermissionCatalog.CreateWorkProfileSnapshot(
            hasSetup: false,
            workProfileAvailable: false);
        var setUp = PermissionCatalog.CreateWorkProfileSnapshot(
            hasSetup: true,
            workProfileAvailable: false);

        Assert.Equal("Создать профиль", notSetUp.RequestLabel);
        Assert.Equal("Проверить профиль", setUp.RequestLabel);
    }

    [Fact]
    public void Create_cross_profile_interaction_snapshot_remains_actionable_when_settings_request_is_blocked()
    {
        var snapshot = PermissionCatalog.CreateCrossProfileInteractionSnapshot(
            isGranted: false,
            canRequestThroughSettings: false);

        Assert.True(snapshot.CanRequest);
        Assert.Equal("Проверить доступ", snapshot.RequestLabel);
    }
}
