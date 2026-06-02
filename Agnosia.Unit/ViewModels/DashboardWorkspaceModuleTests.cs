using Agnosia.Models;
using Agnosia.Unit.TestDoubles;
using Agnosia.Unit.TestSupport;
using Agnosia.ViewModels;
using Xunit;

namespace Agnosia.Unit.ViewModels;

public sealed class DashboardWorkspaceModuleTests
{
    [Fact]
    public async Task Modules_section_is_available_after_dashboard_setup()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            Modules = [TestSnapshots.FileShuttleModule()]
        };
        var viewModel = TestWorkspaceFactory.Create(services);

        await viewModel.EnsureInitializedAsync();
        viewModel.OpenModulesSectionCommand.Execute(null);

        Assert.True(viewModel.CanOpenModulesSection);
        Assert.True(viewModel.IsModulesSectionSelected);
        Assert.Single(viewModel.Modules);
    }

    [Fact]
    public async Task Module_card_opens_details_overlay()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            Modules = [TestSnapshots.FileShuttleModule()]
        };
        var viewModel = TestWorkspaceFactory.Create(services);

        await viewModel.EnsureInitializedAsync();
        var module = Assert.Single(viewModel.Modules);
        module.OpenCommand.Execute(null);

        Assert.True(viewModel.IsModuleDetailsOpen);
        Assert.Same(module, viewModel.SelectedModule);
    }

    [Fact]
    public async Task Missing_module_permission_can_request_permission_and_refresh_after_resume()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            DefaultOperationResult = OperationResult.Success("PermissionOpened"),
            Modules =
            [
                TestSnapshots.FileShuttleModule(
                    requirements:
                    [
                        TestSnapshots.ModuleRequirement(PermissionKind.PersonalAllFiles, false)
                    ],
                    canSetEnabled: false)
            ]
        };
        var viewModel = TestWorkspaceFactory.Create(services);

        await viewModel.EnsureInitializedAsync();
        var module = viewModel.Modules.Single();
        module.OpenCommand.Execute(null);
        var requirement = Assert.Single(module.Requirements);

        await requirement.RequestCommand.ExecuteAsync(null);

        Assert.Equal([PermissionKind.PersonalAllFiles], services.PermissionRequests);
        Assert.Equal(1, services.RequestPermissionCallCount);
        var moduleLoadCountBeforeResume = services.ModuleLoadCount;

        viewModel.HandlePrimaryActivityResumed();

        await AsyncAssert.EventuallyAsync(
            () => services.ModuleLoadCount > moduleLoadCountBeforeResume,
            "Module snapshots should reload after returning from all-files settings.");
    }

    [Fact]
    public async Task Module_toggle_does_not_run_when_requirements_are_missing()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            Modules =
            [
                TestSnapshots.FileShuttleModule(
                    requirements:
                    [
                        TestSnapshots.ModuleRequirement(PermissionKind.PersonalAllFiles, false)
                    ],
                    canSetEnabled: false)
            ]
        };
        var viewModel = TestWorkspaceFactory.Create(services);

        await viewModel.EnsureInitializedAsync();
        var module = viewModel.Modules.Single();

        Assert.False(module.ToggleEnabledCommand.CanExecute(null));
        module.ToggleEnabledCommand.Execute(null);

        Assert.Empty(services.SetModuleEnabledRequests);
    }

    [Fact]
    public async Task Module_toggle_success_updates_module_state()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            Modules = [TestSnapshots.FileShuttleModule(canSetEnabled: true)]
        };
        services.SetModuleEnabledHandler = (module, enabled, _) =>
        {
            services.Modules =
            [
                TestSnapshots.FileShuttleModule(
                    enabled,
                    enabled ? AgnosiaModuleState.Enabled : AgnosiaModuleState.Disabled,
                    canSetEnabled: true)
            ];

            return Task.FromResult(OperationResult.Success(enabled ? "File Shuttle включён." : "File Shuttle выключен."));
        };
        var viewModel = TestWorkspaceFactory.Create(services);

        await viewModel.EnsureInitializedAsync();
        var fileShuttle = viewModel.Modules.Single();

        await fileShuttle.ToggleEnabledCommand.ExecuteAsync(null);

        Assert.Equal([(AgnosiaModuleKind.FileShuttle, true)], services.SetModuleEnabledRequests);
        Assert.False(viewModel.StatusIsError);
        Assert.Equal("File Shuttle включён.", viewModel.StatusMessage);
        Assert.True(viewModel.Modules.Single().IsEnabled);
        Assert.Equal(AgnosiaModuleState.Enabled, viewModel.Modules.Single().State);
    }

    [Fact]
    public async Task Documents_ui_command_is_available_only_when_file_shuttle_is_enabled()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            Modules = [TestSnapshots.FileShuttleModule()]
        };
        var viewModel = TestWorkspaceFactory.Create(services);

        await viewModel.EnsureInitializedAsync();
        var disabledModule = viewModel.Modules.Single();

        Assert.False(disabledModule.OpenDocumentsUiCommand.CanExecute(null));

        services.Modules =
        [
            TestSnapshots.FileShuttleModule(
                true,
                AgnosiaModuleState.Enabled,
                canSetEnabled: true)
        ];

        await viewModel.RefreshCommand.ExecuteAsync(null);
        var enabledModule = viewModel.Modules.Single();

        Assert.True(enabledModule.OpenDocumentsUiCommand.CanExecute(null));

        await enabledModule.OpenDocumentsUiCommand.ExecuteAsync(null);

        Assert.Equal(1, services.OpenDocumentsUiRequests);
    }
}
