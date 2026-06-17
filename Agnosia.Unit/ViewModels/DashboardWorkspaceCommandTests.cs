using Agnosia.Models;
using Agnosia.Unit.TestDoubles;
using Agnosia.Unit.TestSupport;
using Agnosia.ViewModels;
using Xunit;

namespace Agnosia.Unit.ViewModels;

public sealed class DashboardWorkspaceCommandTests
{
    private static readonly AppCommandCase[] AppCommandCases =
    [
        new(
            "Clone",
            "CopyPersonalToWorkFinished",
            CreatePersonalApp,
            app => app.CloneCommand.ExecuteAsync(null),
            (services, exception) => services.CloneHandler = (_, _) => Throw(exception)),
        new(
            "Launch",
            "Launching",
            CreatePersonalApp,
            app => app.LaunchCommand.ExecuteAsync(null),
            (services, exception) => services.LaunchHandler = (_, _) => Throw(exception)),
        new(
            "CreateShortcut",
            "ShortcutRequested",
            CreateWorkApp,
            app => app.CreateShortcutCommand.ExecuteAsync(null),
            (services, exception) => services.CreateShortcutHandler = (_, _) => Throw(exception)),
        new(
            "ToggleFrozen",
            "Hidden",
            CreateWorkApp,
            app => app.ToggleFrozenCommand.ExecuteAsync(null),
            (services, exception) => services.SetFrozenHandler = (_, _, _) => Throw(exception)),
        new(
            "ForceFreeze",
            "ForceHidden",
            CreateWorkApp,
            app => app.ForceFreezeCommand.ExecuteAsync(null),
            (services, exception) => services.ForceFreezeHandler = (_, _) => Throw(exception)),
        new(
            "Uninstall",
            "Deleted",
            CreatePersonalApp,
            app => app.UninstallCommand.ExecuteAsync(null),
            (services, exception) => services.UninstallHandler = (_, _) => Throw(exception)),
        new(
            "ToggleInternetAccess",
            "InternetBlocked",
            CreateWorkApp,
            app => app.ToggleInternetAccessCommand.ExecuteAsync(null),
            (services, exception) => services.SetLockdownInternetAccessHandler = (_, _, _) => Throw(exception))
    ];

    public static TheoryData<AppCommandCase> AppCommandFallbacks
    {
        get
        {
            var data = new TheoryData<AppCommandCase>();
            foreach (var appCommandCase in AppCommandCases) data.Add(appCommandCase);

            return data;
        }
    }

    public static TheoryData<PermissionKind> ResumePermissionKinds =>
    [
        PermissionKind.Notifications,
        PermissionKind.UsageStats,
        PermissionKind.PackageInstall,
        PermissionKind.PersonalAllFiles,
        PermissionKind.WorkAllFiles,
        PermissionKind.Overlay
    ];

    // Проверяет цепочку move: clone, uninstall и refresh dashboard после успеха.
    [Fact]
    public async Task MoveToWorkCommand_clones_then_uninstalls_and_refreshes_dashboard()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            CloneHandler = (_, _) => Task.FromResult(OperationResult.Success("")),
            UninstallHandler = (_, _) => Task.FromResult(OperationResult.Success(""))
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        var app = CreatePersonalApp(viewModel);

        await app.MoveToWorkCommand.ExecuteAsync(null);

        Assert.Single(services.CloneRequests);
        Assert.Single(services.UninstallRequests);
        Assert.Equal(1, services.DashboardProfileLoadCount);
        Assert.False(viewModel.StatusIsError);
        Assert.Equal("MovedToWork", viewModel.StatusMessage);
    }

    // Проверяет, что uninstall не вызывается, если clone для move завершился ошибкой.
    [Fact]
    public async Task MoveToWorkCommand_does_not_uninstall_when_clone_fails()
    {
        var services = new TestPlatformServices
        {
            CloneHandler = (_, _) => Task.FromResult(OperationResult.Failure("CloneRejected"))
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        var app = CreatePersonalApp(viewModel);

        await app.MoveToWorkCommand.ExecuteAsync(null);

        Assert.Single(services.CloneRequests);
        Assert.Empty(services.UninstallRequests);
        Assert.Equal(0, services.DashboardProfileLoadCount);
        Assert.True(viewModel.StatusIsError);
        Assert.Equal("CloneRejected", viewModel.StatusMessage);
    }

    // Проверяет статус частичного успеха move, когда clone успешен, а uninstall упал.
    [Fact]
    public async Task MoveToWorkCommand_reports_delete_failure_after_successful_clone()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            CloneHandler = (_, _) => Task.FromResult(OperationResult.Success("")),
            UninstallHandler = (_, _) => Task.FromResult(OperationResult.Failure("DeleteRejected"))
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        var app = CreatePersonalApp(viewModel);

        await app.MoveToWorkCommand.ExecuteAsync(null);

        Assert.Single(services.CloneRequests);
        Assert.Single(services.UninstallRequests);
        Assert.Equal(1, services.DashboardProfileLoadCount);
        Assert.True(viewModel.StatusIsError);
        Assert.Equal("MovedToWorkDeleteFailed|DeleteRejected", viewModel.StatusMessage);
    }

    // Проверяет fallback message для app-команд при Failure без текста от platform service.
    [Theory]
    [MemberData(nameof(AppCommandFallbacks))]
    public async Task App_commands_use_fallback_message_when_platform_returns_failure(
        AppCommandCase appCommandCase)
    {
        var services = new TestPlatformServices
        {
            DefaultOperationResult = OperationResult.Failure(string.Empty)
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        var app = appCommandCase.CreateApp(viewModel);

        await appCommandCase.ExecuteAsync(app);

        Assert.True(viewModel.StatusIsError);
        Assert.Equal(appCommandCase.FailureFallback, viewModel.StatusMessage);
    }

    // Проверяет общий fallback message для app-команд при exception от platform service.
    [Theory]
    [MemberData(nameof(AppCommandFallbacks))]
    public async Task App_commands_use_generic_fallback_message_when_platform_throws(
        AppCommandCase appCommandCase)
    {
        var exception = new InvalidOperationException("platform unavailable");
        var services = new TestPlatformServices();
        appCommandCase.ThrowFrom(services, exception);
        var viewModel = TestWorkspaceFactory.Create(services);
        var app = appCommandCase.CreateApp(viewModel);

        await appCommandCase.ExecuteAsync(app);

        Assert.True(viewModel.StatusIsError);
        Assert.Equal("GenericOperationFailed", viewModel.StatusMessage);
    }

    // Проверяет refresh dashboard и сохранение ошибки при stale APK сообщении.
    [Fact]
    public async Task Failed_operation_with_stale_apk_message_refreshes_and_preserves_error_hint()
    {
        var staleMessage = "APK изменился: обновите источник установки";
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            CloneHandler = (_, _) => Task.FromResult(OperationResult.Failure(staleMessage))
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        var app = CreatePersonalApp(viewModel);

        await app.CloneCommand.ExecuteAsync(null);

        Assert.Single(services.CloneRequests);
        Assert.Equal(1, services.DashboardProfileLoadCount);
        Assert.True(viewModel.StatusIsError);
        Assert.Equal(staleMessage, viewModel.StatusMessage);
    }

    // Проверяет reload permissions после успешного запроса обычного permission.
    [Fact]
    public async Task Permission_request_success_for_regular_permission_reloads_permissions()
    {
        var services = new TestPlatformServices
        {
            DefaultOperationResult = OperationResult.Success("PermissionOpened"),
            Permissions = [TestSnapshots.GrantedPermission(PermissionKind.WorkProfile)]
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        var permission = TestWorkspaceFactory.CreatePermission(
            viewModel,
            TestSnapshots.RequiredPermission(PermissionKind.WorkProfile));

        await permission.RequestCommand.ExecuteAsync(null);

        Assert.Equal([PermissionKind.WorkProfile], services.PermissionRequests);
        Assert.Equal(1, services.PermissionLoadCount);
        Assert.False(viewModel.StatusIsError);
        Assert.Equal("PermissionOpened", viewModel.StatusMessage);
        Assert.Contains(viewModel.PermissionItems, item => item.Kind == PermissionKind.WorkProfile && item.IsGranted);
    }

    // Проверяет сброс resume flag после неуспешного запроса permission с отложенным refresh.
    [Fact]
    public async Task Permission_request_failure_resets_resume_refresh_flag()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            DefaultOperationResult = OperationResult.Failure("PermissionDenied"),
            Permissions = [TestSnapshots.RequiredPermission(PermissionKind.UsageStats)]
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();
        viewModel.IsPermissionsWindowOpen = true;
        var permission = TestWorkspaceFactory.CreatePermission(
            viewModel,
            TestSnapshots.RequiredPermission(PermissionKind.UsageStats));

        await permission.RequestCommand.ExecuteAsync(null);
        viewModel.HandlePrimaryActivityResumed();

        Assert.Equal([PermissionKind.UsageStats], services.PermissionRequests);
        Assert.Equal(2, services.PermissionLoadCount);
        Assert.True(viewModel.StatusIsError);
        Assert.Equal("PermissionDenied", viewModel.StatusMessage);
    }

    // Проверяет отложенный reload до resume для permissions, открывающих системные экраны.
    [Theory]
    [MemberData(nameof(ResumePermissionKinds))]
    public async Task Permission_request_success_for_settings_permissions_defers_reload_until_resume(
        PermissionKind kind)
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            DefaultOperationResult = OperationResult.Success("PermissionOpened"),
            Permissions = [TestSnapshots.GrantedPermission(kind)]
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();
        viewModel.IsPermissionsWindowOpen = true;
        var permission = TestWorkspaceFactory.CreatePermission(viewModel, TestSnapshots.RequiredPermission(kind));
        var permissionLoadCountBeforeRequest = services.PermissionLoadCount;

        await permission.RequestCommand.ExecuteAsync(null);

        Assert.Equal(permissionLoadCountBeforeRequest, services.PermissionLoadCount);

        var moduleLoadCountBeforeResume = services.ModuleLoadCount;
        viewModel.HandlePrimaryActivityResumed();

        await AsyncAssert.EventuallyAsync(
            () => services.PermissionLoadCount == permissionLoadCountBeforeRequest + 1,
            "Permission reload should happen after primary activity resume.");
        Assert.Equal(moduleLoadCountBeforeResume, services.ModuleLoadCount);
    }

    [Fact]
    public async Task ToggleInternetAccessCommand_updates_local_snapshot_on_success()
    {
        var services = new TestPlatformServices();
        var viewModel = TestWorkspaceFactory.Create(services);
        var app = CreateWorkApp(viewModel);

        await app.ToggleInternetAccessCommand.ExecuteAsync(null);

        var request = Assert.Single(services.SetLockdownInternetAccessRequests);
        Assert.Equal(app.PackageName, request.App.PackageName);
        Assert.True(request.Blocked);
        Assert.True(app.IsInternetBlocked);
        Assert.Equal("UnblockInternet", app.InternetAccessLabel);
        Assert.False(viewModel.StatusIsError);
        Assert.Equal("Ok", viewModel.StatusMessage);
    }

    // Проверяет переход onboarding на финальный шаг, когда все обязательные permissions выданы.
    [Fact]
    public async Task FinishOnboardingCommand_moves_permissions_to_final_when_all_required_permissions_are_granted()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            OnboardingCompleted = false,
            DefaultOperationResult = OperationResult.Success("ProvisioningFinished"),
            Permissions = TestSnapshots.RequiredOnboardingPermissions(granted: true)
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();
        viewModel.OnboardingStep = OnboardingStep.Permissions;

        await viewModel.FinishOnboardingCommand.ExecuteAsync(null);

        Assert.Equal(0, services.CompleteOnboardingCallCount);
        Assert.True(viewModel.IsOnboardingVisible);
        Assert.True(viewModel.IsOnboardingFinalStep);
        Assert.False(viewModel.StatusIsError);
    }

    [Fact]
    public async Task CheckOnboardingWorkProfileCommand_reuses_refresh_permissions_when_profile_becomes_available()
    {
        var services = new TestPlatformServices
        {
            OnboardingCompleted = true,
            DashboardProfile = TestSnapshots.Dashboard(
                hasSetup: false,
                workProfileAvailable: false,
                workProfileState: WorkProfileStateKind.NoWorkProfile),
            Permissions = TestSnapshots.RequiredOnboardingPermissions(granted: false)
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();

        Assert.True(viewModel.IsOnboardingWorkProfileStep);
        Assert.Equal(0, services.PermissionLoadCount);

        services.DashboardProfile = TestSnapshots.Dashboard(
            hasSetup: true,
            workProfileAvailable: true,
            workProfileState: WorkProfileStateKind.Available);

        await viewModel.CheckOnboardingWorkProfileCommand.ExecuteAsync(null);

        await AsyncAssert.EventuallyAsync(
            () => viewModel.OnboardingStep == OnboardingStep.Permissions,
            "Work-profile refresh should advance onboarding to permissions.");
        Assert.Equal(1, services.PermissionLoadCount);
        Assert.True(viewModel.IsOnboardingPermissionsStep);
    }

    // Проверяет завершение onboarding с финального шага.
    [Fact]
    public async Task FinishOnboardingCommand_completes_onboarding_from_final_step()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            OnboardingCompleted = false,
            DefaultOperationResult = OperationResult.Success("ProvisioningFinished"),
            Permissions = TestSnapshots.RequiredOnboardingPermissions(granted: true)
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();
        viewModel.OnboardingStep = OnboardingStep.Final;

        await viewModel.FinishOnboardingCommand.ExecuteAsync(null);

        Assert.Equal(1, services.CompleteOnboardingCallCount);
        Assert.False(viewModel.IsOnboardingVisible);
        Assert.False(viewModel.StatusIsError);
        Assert.Equal("Updated", viewModel.StatusMessage);
    }

    // Проверяет ошибку onboarding, когда часть обязательных permissions не выдана.
    [Fact]
    public async Task FinishOnboardingCommand_reports_failure_when_required_permissions_are_missing()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            OnboardingCompleted = false,
            Permissions =
            [
                TestSnapshots.GrantedPermission(PermissionKind.WorkProfile),
                TestSnapshots.RequiredPermission(PermissionKind.Notifications)
            ]
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();
        viewModel.OnboardingStep = OnboardingStep.Final;

        await viewModel.FinishOnboardingCommand.ExecuteAsync(null);

        Assert.Equal(0, services.CompleteOnboardingCallCount);
        Assert.True(viewModel.IsOnboardingVisible);
        Assert.True(viewModel.StatusIsError);
        Assert.Equal("PermissionRequestFailed", viewModel.StatusMessage);
    }

    // Проверяет, что onboarding остается открытым при ошибке CompleteOnboardingAsync.
    [Fact]
    public async Task FinishOnboardingCommand_keeps_onboarding_open_when_completion_fails()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            OnboardingCompleted = false,
            DefaultOperationResult = OperationResult.Failure("CompletionRejected"),
            Permissions = TestSnapshots.RequiredOnboardingPermissions(granted: true)
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();
        viewModel.OnboardingStep = OnboardingStep.Final;

        await viewModel.FinishOnboardingCommand.ExecuteAsync(null);

        Assert.Equal(1, services.CompleteOnboardingCallCount);
        Assert.True(viewModel.IsOnboardingVisible);
        Assert.True(viewModel.StatusIsError);
        Assert.Equal("CompletionRejected", viewModel.StatusMessage);
    }

    // Проверяет, что модульные permissions не попадают в настройки и onboarding.
    [Fact]
    public async Task Settings_and_onboarding_permissions_exclude_module_permissions()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            OnboardingCompleted = false,
            Permissions =
            [
                .. TestSnapshots.RequiredOnboardingPermissions(granted: true),
                TestSnapshots.RequiredPermission(PermissionKind.PersonalAllFiles),
                TestSnapshots.RequiredPermission(PermissionKind.WorkAllFiles),
                TestSnapshots.RequiredPermission(PermissionKind.VpnControl),
                TestSnapshots.RequiredPermission(PermissionKind.Overlay)
            ]
        };
        var viewModel = TestWorkspaceFactory.Create(services);

        await viewModel.EnsureInitializedAsync();
        await viewModel.StartOnboardingCommand.ExecuteAsync(null);

        Assert.Equal(
            viewModel.OnboardingPermissionItems.Select(item => item.Kind),
            viewModel.PermissionItems.Select(item => item.Kind));
        Assert.Equal(
            [
                PermissionKind.WorkProfile,
                PermissionKind.UsageStats,
                PermissionKind.Notifications,
                PermissionKind.PackageInstall
            ],
            viewModel.PermissionItems.Select(item => item.Kind));
        Assert.DoesNotContain(viewModel.PermissionItems, item => item.Kind == PermissionKind.PersonalAllFiles);
        Assert.DoesNotContain(viewModel.PermissionItems, item => item.Kind == PermissionKind.WorkAllFiles);
        Assert.DoesNotContain(viewModel.PermissionItems, item => item.Kind == PermissionKind.VpnControl);
        Assert.DoesNotContain(viewModel.PermissionItems, item => item.Kind == PermissionKind.Overlay);
        Assert.DoesNotContain(viewModel.OnboardingPermissionItems, item => item.Kind == PermissionKind.PersonalAllFiles);
        Assert.DoesNotContain(viewModel.OnboardingPermissionItems, item => item.Kind == PermissionKind.WorkAllFiles);
        Assert.DoesNotContain(viewModel.OnboardingPermissionItems, item => item.Kind == PermissionKind.VpnControl);
        Assert.DoesNotContain(viewModel.OnboardingPermissionItems, item => item.Kind == PermissionKind.Overlay);
        Assert.True(viewModel.AreOnboardingPermissionsGranted);
        Assert.Equal("GrantedCount|4|4", viewModel.OnboardingPermissionSummary);
    }

    private static AppItemViewModel CreatePersonalApp(DashboardWorkspaceViewModel owner)
    {
        return TestWorkspaceFactory.CreateApp(
            owner,
            ProfileKind.Personal,
            "com.example.notes",
            "Notes",
            isSystem: false);
    }

    private static AppItemViewModel CreateWorkApp(DashboardWorkspaceViewModel owner)
    {
        return TestWorkspaceFactory.CreateApp(
            owner,
            ProfileKind.Work,
            "com.example.work",
            "Work App");
    }

    private static Task<OperationResult> Throw(Exception exception)
    {
        return Task.FromException<OperationResult>(exception);
    }

    public sealed record AppCommandCase(
        string Name,
        string FailureFallback,
        Func<DashboardWorkspaceViewModel, AppItemViewModel> CreateApp,
        Func<AppItemViewModel, Task> ExecuteAsync,
        Action<TestPlatformServices, Exception> ThrowFrom)
    {
        public override string ToString()
        {
            return Name;
        }
    }
}
