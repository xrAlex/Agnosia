using Agnosia.Models;
using Agnosia.Unit.TestDoubles;
using Agnosia.Unit.TestSupport;
using Agnosia.ViewModels;
using Xunit;

namespace Agnosia.Unit.ViewModels;

public sealed class PermissionItemViewModelTests
{
    // Проверяет запрет запроса уже выданного разрешения.
    [Fact]
    public void Granted_permission_cannot_be_requested()
    {
        var item = CreateItem(TestSnapshots.GrantedPermission(PermissionKind.Notifications));

        Assert.True(item.IsGranted);
        Assert.False(item.CanRequest);
        Assert.False(item.RequestCommand.CanExecute(null));
    }

    // Проверяет подписи статуса и кнопки запроса для granted/not granted состояний.
    [Theory]
    [InlineData(true, "Allowed", "Ask", "Allowed", "Allowed")]
    [InlineData(false, "Allowed", "Ask", "ActionRequired", "Ask")]
    public void Labels_reflect_granted_state(
        bool isGranted,
        string grantedLabel,
        string requestLabel,
        string expectedStatusLabel,
        string expectedRequestLabel)
    {
        var item = CreateItem(TestSnapshots.Permission(
            PermissionKind.Overlay,
            isGranted,
            grantedLabel: grantedLabel,
            requestLabel: requestLabel));

        Assert.Equal(expectedStatusLabel, item.StatusLabel);
        Assert.Equal(expectedRequestLabel, item.RequestLabel);
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
