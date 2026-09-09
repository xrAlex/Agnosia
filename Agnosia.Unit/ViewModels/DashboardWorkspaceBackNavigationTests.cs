using Agnosia.Models;
using Agnosia.Unit.TestDoubles;
using Agnosia.Unit.TestSupport;
using Xunit;

namespace Agnosia.Unit.ViewModels;

public sealed class DashboardWorkspaceBackNavigationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Back_closes_app_card_and_keeps_catalog_selected(bool permissionsExpanded)
    {
        var workspace = TestWorkspaceFactory.Create(new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard()
        });
        await workspace.EnsureInitializedAsync();
        workspace.OpenAppsSectionCommand.Execute(null);
        using var app = TestWorkspaceFactory.CreateApp(workspace, ProfileKind.Work);
        app.OpenControlsCommand.Execute(null);
        app.IsPermissionDetailsExpanded = permissionsExpanded;

        Assert.True(workspace.TryHandleBack());

        Assert.False(workspace.IsAppControlWindowOpen);
        Assert.Null(workspace.SelectedApp);
        Assert.True(workspace.IsAppsSectionSelected);
        Assert.False(workspace.TryHandleBack());
    }

    [Fact]
    public async Task Back_closes_only_top_overlay_and_releases_selected_module()
    {
        var workspace = TestWorkspaceFactory.Create(new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            Modules = [TestSnapshots.FileShuttleModule()]
        });
        await workspace.EnsureInitializedAsync();
        workspace.IsLogWindowOpen = true;
        workspace.IsPermissionsWindowOpen = true;
        Assert.Single(workspace.Modules).OpenCommand.Execute(null);
        using var app = TestWorkspaceFactory.CreateApp(workspace, ProfileKind.Work);
        app.OpenControlsCommand.Execute(null);

        Assert.True(workspace.TryHandleBack());
        Assert.False(workspace.IsAppControlWindowOpen);
        Assert.True(workspace.IsModuleDetailsOpen);
        Assert.True(workspace.IsPermissionsWindowOpen);
        Assert.True(workspace.IsLogWindowOpen);

        Assert.True(workspace.TryHandleBack());
        Assert.Null(workspace.SelectedModule);
        Assert.False(workspace.IsModuleDetailsOpen);
        Assert.True(workspace.IsPermissionsWindowOpen);
        Assert.True(workspace.IsLogWindowOpen);

        Assert.True(workspace.TryHandleBack());
        Assert.False(workspace.IsPermissionsWindowOpen);
        Assert.True(workspace.IsLogWindowOpen);

        Assert.True(workspace.TryHandleBack());
        Assert.False(workspace.IsLogWindowOpen);
        Assert.False(workspace.TryHandleBack());
    }

    [Fact]
    public async Task Back_dismisses_recovery_before_app_card()
    {
        var workspace = TestWorkspaceFactory.Create(new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(
                workProfileRecovery: WorkProfileRecoveryKind.DeleteWorkProfile)
        });
        await workspace.EnsureInitializedAsync();
        using var app = TestWorkspaceFactory.CreateApp(workspace);
        app.OpenControlsCommand.Execute(null);

        Assert.True(workspace.TryHandleBack());

        Assert.False(workspace.IsWorkProfileRecoveryVisible);
        Assert.True(workspace.IsAppControlWindowOpen);
        Assert.Same(app, workspace.SelectedApp);
    }
}
