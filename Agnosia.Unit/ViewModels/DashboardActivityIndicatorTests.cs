using Agnosia.Models;
using Agnosia.Unit.TestDoubles;
using Agnosia.Unit.TestSupport;
using Xunit;

namespace Agnosia.Unit.ViewModels;

public sealed class DashboardActivityIndicatorTests
{
    [Fact]
    public async Task Closing_app_dialog_keeps_indicator_active_until_permission_read_finishes()
    {
        var permissions = new TaskCompletionSource<IReadOnlyList<AppPermissionSnapshot>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            AppInventory = new DashboardAppInventorySnapshot([TestSnapshots.App(ProfileKind.Personal)], []),
            LoadAppPermissionsHandler = (_, _) => permissions.Task
        };
        var workspace = TestWorkspaceFactory.Create(services);
        await workspace.EnsureInitializedAsync();
        await AsyncAssert.EventuallyAsync(() => workspace.VisibleApps.Count == 1 && !workspace.IsActivityIndicatorActive,
            "Initial catalog should finish loading.");
        var app = workspace.VisibleApps[0];
        workspace.OpenAppControl(app);

        var refresh = app.Permissions.RefreshCommand.ExecuteAsync(null);
        Assert.True(workspace.IsActivityIndicatorActive);
        workspace.CloseAppControl();
        Assert.Null(workspace.SelectedApp);
        Assert.True(workspace.IsActivityIndicatorActive);

        permissions.SetResult([]);
        await refresh;
        Assert.False(workspace.IsActivityIndicatorActive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Indicator_covers_background_inventory_until_success_or_failure(bool fail)
    {
        var inventory = new TaskCompletionSource<DashboardAppInventorySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            LoadAppInventoryHandler = (_, _) => inventory.Task
        };
        var workspace = TestWorkspaceFactory.Create(services);
        var states = new List<bool>();
        workspace.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(workspace.IsActivityIndicatorActive))
                states.Add(workspace.IsActivityIndicatorActive);
        };

        await workspace.EnsureInitializedAsync();

        Assert.False(workspace.IsDashboardRefreshing);
        Assert.False(workspace.IsAppInventoryProgressVisible);
        Assert.True(workspace.IsActivityIndicatorActive);
        Assert.Contains(true, states);

        if (fail) inventory.SetException(new InvalidOperationException("Inventory unavailable"));
        else inventory.SetResult(DashboardAppInventorySnapshot.Empty);

        await AsyncAssert.EventuallyAsync(() => !workspace.IsActivityIndicatorActive, "Indicator should stop when inventory completes.");
        Assert.False(states[^1]);
    }
}
