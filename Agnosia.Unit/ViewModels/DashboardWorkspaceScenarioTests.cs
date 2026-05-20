using Agnosia.Models;
using Agnosia.Unit.TestDoubles;
using Agnosia.Unit.TestSupport;
using Xunit;

namespace Agnosia.Unit.ViewModels;

public sealed class DashboardWorkspaceScenarioTests
{
    // Проверяет копирование личного приложения в рабочий профиль и refresh состояния.
    [Fact]
    public async Task CloneCommand_copies_personal_app_to_work_profile_and_refreshes_dashboard()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(
                hasSetup: false,
                workProfileAvailable: false,
                workProfileState: WorkProfileStateKind.NoWorkProfile),
            DefaultOperationResult = OperationResult.Success(string.Empty)
        };
        var owner = TestWorkspaceFactory.Create(services);
        var app = TestWorkspaceFactory.CreateApp(
            owner,
            ProfileKind.Personal,
            "com.example.notes",
            "Notes",
            isSystem: false);

        await app.CloneCommand.ExecuteAsync(null);

        var request = Assert.Single(services.CloneRequests);
        Assert.Equal("com.example.notes", request.PackageName);
        Assert.Equal(ProfileKind.Personal, request.Profile);
        Assert.Empty(services.UninstallRequests);
        Assert.Equal(1, services.DashboardProfileLoadCount);
        Assert.False(owner.StatusIsError);
    }

    // Проверяет запрос создания ярлыка для приложения рабочего профиля.
    [Fact]
    public async Task CreateShortcutCommand_requests_shortcut_for_work_profile_app()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(
                hasSetup: false,
                workProfileAvailable: false,
                workProfileState: WorkProfileStateKind.NoWorkProfile),
            DefaultOperationResult = OperationResult.Success("Shortcut requested")
        };
        var owner = TestWorkspaceFactory.Create(services);
        var app = TestWorkspaceFactory.CreateApp(
            owner,
            ProfileKind.Work,
            "com.example.work",
            "Work App",
            isHidden: true,
            canLaunch: false);

        await app.CreateShortcutCommand.ExecuteAsync(null);

        var request = Assert.Single(services.CreateShortcutRequests);
        Assert.Equal("com.example.work", request.PackageName);
        Assert.Equal(ProfileKind.Work, request.Profile);
        Assert.True(app.ShowWorkControls);
        Assert.False(owner.StatusIsError);
    }

    // Проверяет запуск скрытого приложения рабочего профиля из shortcut flow.
    [Fact]
    public async Task LaunchCommand_launches_hidden_work_profile_app_from_shortcut_flow()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(
                hasSetup: false,
                workProfileAvailable: false,
                workProfileState: WorkProfileStateKind.NoWorkProfile),
            DefaultOperationResult = OperationResult.Success("Launching")
        };
        var owner = TestWorkspaceFactory.Create(services);
        var app = TestWorkspaceFactory.CreateApp(
            owner,
            ProfileKind.Work,
            "com.example.hidden",
            "Hidden Work App",
            isHidden: true,
            canLaunch: false);

        await app.LaunchCommand.ExecuteAsync(null);

        var request = Assert.Single(services.LaunchRequests);
        Assert.Equal("com.example.hidden", request.PackageName);
        Assert.Equal(ProfileKind.Work, request.Profile);
        Assert.True(request.IsHidden);
        Assert.True(app.ShowLaunch);
        Assert.False(owner.StatusIsError);
    }

    // Проверяет состояние VM при включении отключения VPN перед запуском и автозапуска после freeze.
    [Fact]
    public void Vpn_settings_update_disable_before_work_launch_and_enable_after_freeze_state()
    {
        var services = new TestPlatformServices();
        var viewModel = TestWorkspaceFactory.Create(services);

        viewModel.DisableVpnBeforeWorkLaunch = true;
        viewModel.EnableVpnAfterWorkFreeze = true;
        viewModel.SelectTunguskaVpnAfterFreezeCommand.Execute(null);
        viewModel.TunguskaAutomationToken = "unit-token";

        Assert.True(viewModel.DisableVpnBeforeWorkLaunch);
        Assert.True(viewModel.EnableVpnAfterWorkFreeze);
        Assert.Equal("unit-token", viewModel.TunguskaAutomationToken);
        Assert.True(viewModel.IsVpnAfterFreezeClientPickerVisible);
        Assert.True(viewModel.IsTunguskaVpnAfterFreezeSelected);
        Assert.True(viewModel.IsTunguskaAutomationTokenVisible);
        Assert.False(viewModel.IsToggleOnlyVpnAfterFreezeWarningVisible);
        Assert.Empty(services.SavedSettings);
    }

    // Проверяет warning для VPN-клиентов, у которых есть только toggle-команда.
    [Fact]
    public async Task Toggle_only_vpn_clients_show_warning_when_enable_after_freeze_is_on()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(
                hasSetup: false,
                workProfileAvailable: false,
                workProfileState: WorkProfileStateKind.NoWorkProfile)
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();

        viewModel.EnableVpnAfterWorkFreeze = true;
        viewModel.SelectHappVpnAfterFreezeCommand.Execute(null);

        Assert.True(viewModel.IsHappVpnAfterFreezeSelected);
        Assert.True(viewModel.IsToggleOnlyVpnAfterFreezeWarningVisible);
        Assert.False(viewModel.IsTunguskaAutomationTokenVisible);
    }

    // Проверяет применение quiet mode recovery при загрузке состояния рабочего профиля.
    [Fact]
    public async Task EnsureInitializedAsync_applies_work_profile_quiet_mode_recovery_state()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(
                hasSetup: false,
                workProfileAvailable: false,
                workProfileState: WorkProfileStateKind.WorkProfileQuietMode,
                workProfileRecovery: WorkProfileRecoveryKind.WorkProfileQuietMode,
                workProfileDiagnosticReason: "quiet mode is active")
        };
        var viewModel = TestWorkspaceFactory.Create(services);

        await viewModel.EnsureInitializedAsync();

        Assert.False(viewModel.HasSetup);
        Assert.False(viewModel.WorkProfileAvailable);
        Assert.Equal("QuietMode", viewModel.WorkProfileStatusText);
        Assert.True(viewModel.IsWorkProfileRecoveryVisible);
        Assert.Equal("Рабочий профиль выключен", viewModel.WorkProfileRecoveryTitle);
        Assert.Contains("quiet mode is active", viewModel.WorkProfileRecoveryMessage, StringComparison.Ordinal);
        Assert.False(viewModel.CanContinueOnboardingFromWorkProfile);
    }

    // Проверяет старт онбординга на поддержанном устройстве без рабочего профиля.
    [Fact]
    public async Task EnsureInitializedAsync_starts_onboarding_for_supported_device_without_work_profile()
    {
        var services = new TestPlatformServices
        {
            OnboardingCompleted = true,
            DashboardProfile = TestSnapshots.Dashboard(
                hasSetup: false,
                workProfileAvailable: false,
                workProfileState: WorkProfileStateKind.NoWorkProfile)
        };
        var viewModel = TestWorkspaceFactory.Create(services);

        await viewModel.EnsureInitializedAsync();

        Assert.True(viewModel.IsOnboardingVisible);
        Assert.True(viewModel.IsOnboardingWelcomeStep);
        Assert.Equal("1", viewModel.OnboardingStepLabel);
        Assert.True(viewModel.CanStartProvisioning);
    }

    // Проверяет вызов platform provisioning, когда setup можно начать.
    [Fact]
    public async Task StartProvisioningCommand_calls_onboarding_service_when_device_can_start_setup()
    {
        var services = new TestPlatformServices
        {
            OnboardingCompleted = true,
            DashboardProfile = TestSnapshots.Dashboard(
                hasSetup: false,
                workProfileAvailable: false,
                workProfileState: WorkProfileStateKind.NoWorkProfile),
            DefaultOperationResult = OperationResult.Success("ProvisioningStarted")
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();

        await viewModel.StartProvisioningCommand.ExecuteAsync(null);

        Assert.Equal(1, services.StartProvisioningCallCount);
        Assert.False(viewModel.StatusIsError);
        Assert.True(viewModel.IsOnboardingVisible);
    }

    // Проверяет переход онбординга с welcome на шаг рабочего профиля.
    [Fact]
    public void StartOnboardingCommand_moves_welcome_step_to_work_profile_step()
    {
        var viewModel = TestWorkspaceFactory.Create();

        viewModel.StartOnboardingCommand.Execute(null);

        Assert.Equal(OnboardingStep.WorkProfile, viewModel.OnboardingStep);
        Assert.True(viewModel.IsOnboardingWorkProfileStep);
        Assert.Equal("2", viewModel.OnboardingStepLabel);
    }

    // Проверяет guard перехода к permissions, пока рабочий профиль недоступен.
    [Fact]
    public async Task ContinueOnboardingToPermissionsCommand_requires_available_work_profile()
    {
        var services = new TestPlatformServices();
        var viewModel = TestWorkspaceFactory.Create(services);
        viewModel.OnboardingStep = OnboardingStep.WorkProfile;
        viewModel.WorkProfileAvailable = false;

        await viewModel.ContinueOnboardingToPermissionsCommand.ExecuteAsync(null);

        Assert.Equal(OnboardingStep.WorkProfile, viewModel.OnboardingStep);
        Assert.Equal(0, services.PermissionLoadCount);
        Assert.True(viewModel.StatusIsError);
        Assert.Equal("CompleteProvisioning", viewModel.StatusMessage);
    }
}
