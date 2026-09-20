using Agnosia.Models;
using Agnosia.Unit.TestDoubles;
using Agnosia.Unit.TestSupport;
using Agnosia.ViewModels;
using Xunit;

namespace Agnosia.Unit.ViewModels;

public sealed class DashboardWorkspaceRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_permission_read_does_not_complete_onboarding_using_stale_grants(bool canceled)
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            OnboardingCompleted = false,
            Permissions = TestSnapshots.RequiredOnboardingPermissions(granted: true)
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();
        viewModel.OnboardingStep = OnboardingStep.Final;
        services.LoadPermissionsHandler = _ => Task.FromException<IReadOnlyList<PermissionSnapshot>>(
            canceled ? new OperationCanceledException() : new InvalidOperationException());

        await viewModel.OpenPermissionsCommand.ExecuteAsync(null);
        await viewModel.FinishOnboardingCommand.ExecuteAsync(null);

        Assert.True(viewModel.StatusIsError);
        Assert.True(viewModel.IsOnboardingVisible);
        Assert.Equal(0, services.CompleteOnboardingCallCount);
    }

    [Fact]
    public async Task Module_failure_preserves_state_when_reloading_also_fails()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            Modules = [TestSnapshots.FileShuttleModule()]
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();
        var module = Assert.Single(viewModel.Modules);
        services.SetModuleEnabledHandler = (_, _, _) => Task.FromException<OperationResult>(new InvalidOperationException());
        services.LoadModulesHandler = _ => Task.FromException<IReadOnlyList<AgnosiaModuleSnapshot>>(new InvalidOperationException());

        await module.ToggleEnabledCommand.ExecuteAsync(null);

        Assert.True(viewModel.StatusIsError);
        Assert.Same(module, Assert.Single(viewModel.Modules));
        services.LoadModulesHandler = null;
        viewModel.HandlePrimaryActivityResumed();
        await AsyncAssert.EventuallyAsync(() => !viewModel.StatusIsError, "Resume should recover module state.");
    }

    [Fact]
    public async Task Resume_retries_permission_refresh_after_transient_failure()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            Permissions = TestSnapshots.RequiredOnboardingPermissions(granted: false)
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();
        viewModel.IsPermissionsWindowOpen = true;
        var permission = TestWorkspaceFactory.CreatePermission(
            viewModel, TestSnapshots.RequiredPermission(PermissionKind.UsageStats));
        await permission.RequestCommand.ExecuteAsync(null);
        services.LoadPermissionsHandler = _ => Task.FromException<IReadOnlyList<PermissionSnapshot>>(
            new InvalidOperationException("Temporary permission query failure"));
        viewModel.HandlePrimaryActivityResumed();
        await AsyncAssert.EventuallyAsync(() => viewModel.StatusIsError, "Permission refresh should report its failure.");
        services.LoadPermissionsHandler = null;
        services.Permissions = TestSnapshots.RequiredOnboardingPermissions(granted: true);

        viewModel.HandlePrimaryActivityResumed();

        await AsyncAssert.EventuallyAsync(
            () => !viewModel.StatusIsError && viewModel.OnboardingPermissionSummary == "GrantedCount|4|4",
            "A subsequent resume should retry a failed permission refresh.");
    }

    [Fact]
    public async Task Permission_resume_waits_for_request_before_reloading_authoritative_permissions()
    {
        var completion = new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            Permissions = TestSnapshots.RequiredOnboardingPermissions(granted: false),
            RequestPermissionHandler = (_, _) => completion.Task
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();
        viewModel.IsPermissionsWindowOpen = true;
        var permission = TestWorkspaceFactory.CreatePermission(
            viewModel, TestSnapshots.RequiredPermission(PermissionKind.UsageStats));
        var loads = services.PermissionLoadCount;
        var request = permission.RequestCommand.ExecuteAsync(null);

        viewModel.HandlePrimaryActivityResumed();
        services.Permissions = TestSnapshots.RequiredOnboardingPermissions(granted: true);
        completion.SetResult(OperationResult.Success("PermissionOpened"));
        await request;

        await AsyncAssert.EventuallyAsync(
            () => viewModel.OnboardingPermissionSummary == "GrantedCount|4|4",
            "The permission result must complete before resume refresh reads the granted state.");
        Assert.Equal(loads + 1, services.PermissionLoadCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_refresh_keeps_last_complete_profile_and_permissions(bool canceled)
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(workProfileAvailable: true),
            Permissions = TestSnapshots.RequiredOnboardingPermissions(granted: true)
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();
        viewModel.SelectWorkCommand.Execute(null);
        services.DashboardProfile = TestSnapshots.Dashboard(
            workProfileAvailable: false, workProfileState: WorkProfileStateKind.Unavailable);
        services.LoadPermissionsHandler = _ => Task.FromException<IReadOnlyList<PermissionSnapshot>>(
            canceled ? new OperationCanceledException() : new InvalidOperationException("Transient failure"));

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(viewModel.StatusIsError);
        Assert.True(viewModel.WorkProfileAvailable);
        Assert.True(viewModel.IsWorkProfileSelected);
        Assert.Equal("GrantedCount|4|4", viewModel.OnboardingPermissionSummary);
    }

    [Fact]
    public async Task Resume_recovers_after_failed_initial_refresh()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(workProfileAvailable: true),
            Permissions = TestSnapshots.RequiredOnboardingPermissions(granted: true),
            LoadPermissionsHandler = _ => Task.FromException<IReadOnlyList<PermissionSnapshot>>(
                new InvalidOperationException("Transient failure"))
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();
        Assert.True(viewModel.StatusIsError);
        services.LoadPermissionsHandler = null;

        viewModel.HandlePrimaryActivityResumed();

        await AsyncAssert.EventuallyAsync(
            () => !viewModel.StatusIsError && viewModel.OnboardingPermissionSummary == "GrantedCount|4|4",
            "Resume should recover the dashboard after a transient refresh failure.");
        Assert.True(viewModel.WorkProfileAvailable);
    }
}
