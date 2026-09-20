using Agnosia.Models;
using Agnosia.Unit.TestDoubles;
using Agnosia.Unit.TestSupport;
using Agnosia.ViewModels;
using Xunit;

namespace Agnosia.Unit.ViewModels;

public sealed class PrereleaseIsolationRegressionTests
{
    [Fact]
    public async Task Freeze_callback_during_command_refreshes_card_after_command_finishes()
    {
        var commandStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commandCompleted = new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            AppInventory = new DashboardAppInventorySnapshot([], [
                TestSnapshots.App(ProfileKind.Work) with { IsIsolationEnabled = true }
            ]),
            CreateShortcutHandler = (_, _) =>
            {
                commandStarted.SetResult();
                return commandCompleted.Task;
            }
        };
        var workspace = TestWorkspaceFactory.Create(services);
        await workspace.EnsureInitializedAsync();
        workspace.SelectWorkCommand.Execute(null);
        await AsyncAssert.EventuallyAsync(() => workspace.VisibleApps.Count == 1, "Work inventory should load.");
        var app = workspace.VisibleApps[0];
        workspace.OpenAppControl(app);
        var command = app.CreateShortcutCommand.ExecuteAsync(null);
        await commandStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        services.AppInventory = new DashboardAppInventorySnapshot([], [app.Snapshot with { IsHidden = true }]);

        workspace.HandleWorkAppFrozen(app.PackageName);
        commandCompleted.SetResult(OperationResult.Success("Ok"));
        await command;

        await AsyncAssert.EventuallyAsync(() => app.IsHidden, "Callback refresh must survive an in-flight command.");
        Assert.Same(app, workspace.SelectedApp);
    }

    [Fact]
    public void Temporarily_visible_work_app_keeps_isolation_controls()
    {
        var snapshot = TestSnapshots.App(ProfileKind.Work) with { IsIsolationEnabled = true };
        var app = TestWorkspaceFactory.CreateApp(TestWorkspaceFactory.Create(), snapshot);

        Assert.False(app.IsHidden);
        Assert.True(app.IsAgnosiaManaged);
        Assert.True(app.ForceFreezeCommand.CanExecute(null));
        Assert.True(app.CreateShortcutCommand.CanExecute(null));
    }

    [Fact]
    public void Disabling_isolation_notifies_card_when_hidden_state_does_not_change()
    {
        var snapshot = TestSnapshots.App(ProfileKind.Work) with { IsIsolationEnabled = true };
        var app = TestWorkspaceFactory.CreateApp(TestWorkspaceFactory.Create(), snapshot);
        var changed = new List<string?>();
        app.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        app.ApplySnapshot(snapshot with { IsIsolationEnabled = false });

        Assert.False(app.IsAgnosiaManaged);
        Assert.Contains(nameof(AppItemViewModel.IsAgnosiaManaged), changed);
    }

    [Fact]
    public async Task Isolation_toggle_disables_temporarily_visible_session()
    {
        bool? requestedHidden = null;
        var services = new TestPlatformServices
        {
            SetFrozenHandler = (_, hidden, _) =>
            {
                requestedHidden = hidden;
                return Task.FromResult(OperationResult.Success("Ok"));
            }
        };
        var workspace = TestWorkspaceFactory.Create(services);
        var app = TestWorkspaceFactory.CreateApp(workspace,
            TestSnapshots.App(ProfileKind.Work) with { IsIsolationEnabled = true });

        await app.ToggleFrozenCommand.ExecuteAsync(null);

        Assert.Equal(false, requestedHidden);
        Assert.False(app.IsHidden);
        Assert.False(app.IsAgnosiaManaged);
    }

    [Fact]
    public async Task Freeze_callback_updates_open_card_without_replacing_it()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            AppInventory = new DashboardAppInventorySnapshot([], [
                TestSnapshots.App(ProfileKind.Work) with { IsIsolationEnabled = true }
            ])
        };
        var workspace = TestWorkspaceFactory.Create(services);
        await workspace.EnsureInitializedAsync();
        workspace.SelectWorkCommand.Execute(null);
        await AsyncAssert.EventuallyAsync(() => workspace.VisibleApps.Count == 1, "Work inventory should load.");
        var app = workspace.VisibleApps[0];
        workspace.OpenAppControl(app);
        services.AppInventory = new DashboardAppInventorySnapshot([], [app.Snapshot with { IsHidden = true }]);

        workspace.HandleWorkAppFrozen(app.PackageName);
        await AsyncAssert.EventuallyAsync(() => app.IsHidden, "Frozen state should reach the open card.");

        Assert.Same(app, workspace.SelectedApp);
        Assert.True(workspace.IsAppControlWindowOpen);
        Assert.True(app.IsHidden);
        Assert.True(app.IsAgnosiaManaged);
    }

    [Fact]
    public async Task Delayed_freeze_callback_keeps_new_active_session_visible()
    {
        var active = TestSnapshots.App(ProfileKind.Work) with { IsIsolationEnabled = true };
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            AppInventory = new DashboardAppInventorySnapshot([], [active])
        };
        var workspace = TestWorkspaceFactory.Create(services);
        await workspace.EnsureInitializedAsync();
        workspace.SelectWorkCommand.Execute(null);
        await AsyncAssert.EventuallyAsync(() => workspace.VisibleApps.Count == 1, "Work inventory should load.");
        var app = workspace.VisibleApps[0];
        var previousLoads = services.AppInventoryLoadCount;

        workspace.HandleWorkAppFrozen(app.PackageName);

        await AsyncAssert.EventuallyAsync(() => services.AppInventoryLoadCount > previousLoads,
            "A callback should reload authoritative inventory.");
        Assert.False(app.IsHidden);
        Assert.True(app.IsAgnosiaManaged);
    }
}
