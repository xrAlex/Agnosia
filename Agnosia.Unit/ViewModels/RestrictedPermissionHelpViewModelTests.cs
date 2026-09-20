using Agnosia.Models;
using Agnosia.Unit.TestDoubles;
using Agnosia.Unit.TestSupport;
using Agnosia.ViewModels;
using Xunit;

namespace Agnosia.Unit.ViewModels;

public sealed class RestrictedPermissionHelpViewModelTests
{
    [Theory]
    [InlineData(PermissionKind.UsageStats, false, true, true, true)]
    [InlineData(PermissionKind.Overlay, false, true, true, true)]
    [InlineData(PermissionKind.UsageStats, false, false, true, false)]
    [InlineData(PermissionKind.UsageStats, true, true, false, false)]
    [InlineData(PermissionKind.Overlay, true, true, false, false)]
    [InlineData(PermissionKind.Notifications, false, true, false, false)]
    [InlineData(PermissionKind.PackageInstall, false, true, false, false)]
    [InlineData(null, false, true, false, false)]
    public async Task Help_is_available_only_for_missing_sensitive_permissions(
        PermissionKind? kind, bool granted, bool canRequest, bool visible, bool canOpen)
    {
        var services = new TestPlatformServices();
        var help = new RestrictedPermissionHelpViewModel(TestWorkspaceFactory.Create(services), kind, granted, canRequest);

        Assert.Equal(visible, help.IsVisible);
        Assert.False(help.IsExpanded);
        Assert.Equal(canOpen, help.OpenSettingsCommand.CanExecute(null));
        if (!canOpen)
        {
            await help.OpenSettingsCommand.ExecuteAsync(null);
            Assert.Empty(services.AppDetailsSettingsRequests);
        }
    }

    [Theory]
    [InlineData(PermissionKind.UsageStats, ProfileKind.Work)]
    [InlineData(PermissionKind.Overlay, ProfileKind.Personal)]
    public async Task Settings_open_in_the_profile_that_needs_permission(PermissionKind kind, ProfileKind expectedProfile)
    {
        var services = new TestPlatformServices();
        var item = TestWorkspaceFactory.CreatePermission(TestWorkspaceFactory.Create(services), TestSnapshots.RequiredPermission(kind));

        await item.RestrictedSettingsHelp.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal([expectedProfile], services.AppDetailsSettingsRequests);
        Assert.False(item.IsGranted);
        Assert.Empty(services.PermissionRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_settings_launch_preserves_error_and_does_not_schedule_permission_refresh(bool throws)
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            OpenAppDetailsSettingsHandler = (_, _) => throws
                ? throw new InvalidOperationException("Settings unavailable")
                : Task.FromResult(OperationResult.Failure("Откройте настройки вручную."))
        };
        var owner = TestWorkspaceFactory.Create(services);
        await owner.EnsureInitializedAsync();
        owner.IsPermissionsWindowOpen = true;
        var help = new RestrictedPermissionHelpViewModel(owner, PermissionKind.UsageStats, false, true);
        var loads = services.PermissionLoadCount;

        await help.OpenSettingsCommand.ExecuteAsync(null);
        owner.HandlePrimaryActivityResumed();

        Assert.True(owner.StatusIsError);
        Assert.Equal(throws ? "AppDetailsSettingsFailed" : "Откройте настройки вручную.", owner.StatusMessage);
        Assert.Equal(loads, services.PermissionLoadCount);
    }

    [Fact]
    public async Task Resume_during_settings_launch_is_processed_after_command_completion()
    {
        var completion = new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            Permissions = [TestSnapshots.RequiredPermission(PermissionKind.UsageStats)],
            OpenAppDetailsSettingsHandler = (_, _) => completion.Task
        };
        var owner = TestWorkspaceFactory.Create(services);
        await owner.EnsureInitializedAsync();
        owner.IsPermissionsWindowOpen = true;
        var help = new RestrictedPermissionHelpViewModel(owner, PermissionKind.UsageStats, false, true);
        var loads = services.PermissionLoadCount;

        var opening = help.OpenSettingsCommand.ExecuteAsync(null);
        owner.HandlePrimaryActivityResumed();
        Assert.Equal(loads, services.PermissionLoadCount);
        completion.SetResult(OperationResult.Success("Settings opened"));
        await opening;

        await AsyncAssert.EventuallyAsync(() => services.PermissionLoadCount == loads + 1,
            "The pending resume should refresh permissions after the settings command finishes.");
        Assert.False(owner.PermissionItems.Single(item => item.Kind == PermissionKind.UsageStats).IsGranted);
    }

    [Fact]
    public async Task Overlay_help_in_module_opens_personal_settings_and_disappears_after_access_is_granted()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            Modules = [TestSnapshots.VpnGuardModule(requirements: [TestSnapshots.ModuleRequirement(PermissionKind.Overlay, false)])]
        };
        var owner = TestWorkspaceFactory.Create(services);
        await owner.EnsureInitializedAsync();
        var module = Assert.Single(owner.Modules);
        module.OpenCommand.Execute(null);
        var help = Assert.Single(module.Requirements).RestrictedSettingsHelp;
        Assert.True(help.IsVisible);

        await help.OpenSettingsCommand.ExecuteAsync(null);
        Assert.Equal([ProfileKind.Personal], services.AppDetailsSettingsRequests);
        services.Modules = [TestSnapshots.VpnGuardModule(requirements: [TestSnapshots.ModuleRequirement(PermissionKind.Overlay, true)])];
        owner.HandlePrimaryActivityResumed();

        await AsyncAssert.EventuallyAsync(() => module.Requirements.Single().IsSatisfied,
            "Returning from overlay settings should update the module requirements.");
        Assert.False(module.Requirements.Single().RestrictedSettingsHelp.IsVisible);
    }
}
