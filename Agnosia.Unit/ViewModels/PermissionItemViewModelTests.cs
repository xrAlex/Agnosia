using Agnosia.Models;
using Agnosia.Unit.TestDoubles;
using Agnosia.Unit.TestSupport;
using Agnosia.ViewModels;
using Xunit;

namespace Agnosia.Unit.ViewModels;

public sealed class PermissionItemViewModelTests
{
    [Theory]
    [InlineData(PermissionKind.UsageStats)]
    public async Task App_settings_return_refreshes_permission_without_reloading_apps(PermissionKind kind)
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            Permissions = [TestSnapshots.RequiredPermission(kind)]
        };
        var owner = TestWorkspaceFactory.Create(services);
        await owner.EnsureInitializedAsync();
        owner.IsPermissionsWindowOpen = true;
        var item = TestWorkspaceFactory.CreatePermission(owner, TestSnapshots.RequiredPermission(kind));
        var permissionLoads = services.PermissionLoadCount;
        var appLoads = services.AppInventoryLoadCount;

        await item.RestrictedSettingsHelp.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal(permissionLoads, services.PermissionLoadCount);
        Assert.False(item.IsGranted);
        services.Permissions = [TestSnapshots.GrantedPermission(kind)];
        owner.HandlePrimaryActivityResumed();

        await AsyncAssert.EventuallyAsync(
            () => owner.PermissionItems.Any(permission => permission.Kind == kind && permission.IsGranted),
            "Returning from app settings should update the permission.");
        Assert.Equal(permissionLoads + 1, services.PermissionLoadCount);
        Assert.Equal(appLoads, services.AppInventoryLoadCount);
    }

    // Проверяет запрет запроса уже выданного разрешения.
    [Fact]
    public void Granted_permission_cannot_be_requested()
    {
        var item = CreateItem(TestSnapshots.GrantedPermission(PermissionKind.Notifications));

        Assert.True(item.IsGranted);
        Assert.False(item.CanRequest);
        Assert.False(item.RequestCommand.CanExecute(null));
    }

    // Проверяет, что command не вызывает owner, когда запрос разрешения запрещен.
    [Fact]
    public async Task RequestCommand_does_not_call_owner_when_request_is_forbidden()
    {
        var services = new TestPlatformServices();
        var owner = TestWorkspaceFactory.Create(services);
        var item = TestWorkspaceFactory.CreatePermission(
            owner,
            PermissionKind.PackageInstall,
            isGranted: false,
            canRequest: false);

        await item.RequestCommand.ExecuteAsync(null);

        Assert.False(item.CanRequest);
        Assert.Equal(0, services.RequestPermissionCallCount);
    }

    private static PermissionItemViewModel CreateItem(PermissionSnapshot snapshot)
    {
        return TestWorkspaceFactory.CreatePermission(TestWorkspaceFactory.Create(), snapshot);
    }
}
