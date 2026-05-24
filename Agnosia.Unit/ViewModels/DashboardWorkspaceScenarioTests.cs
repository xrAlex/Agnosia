using Agnosia.Models;
using Agnosia.Unit.TestDoubles;
using Agnosia.Unit.TestSupport;
using Agnosia.ViewModels;
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
        SelectVpnAfterFreezeClient(viewModel, VpnAutomationClientKind.Tunguska);
        viewModel.TunguskaAutomationToken = "unit-token";

        Assert.True(viewModel.DisableVpnBeforeWorkLaunch);
        Assert.True(viewModel.EnableVpnAfterWorkFreeze);
        Assert.Equal("unit-token", viewModel.TunguskaAutomationToken);
        Assert.True(viewModel.IsVpnAfterFreezeClientPickerVisible);
        Assert.True(IsVpnAfterFreezeClientSelected(viewModel, VpnAutomationClientKind.Tunguska));
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
        SelectVpnAfterFreezeClient(viewModel, VpnAutomationClientKind.Happ);

        Assert.True(IsVpnAfterFreezeClientSelected(viewModel, VpnAutomationClientKind.Happ));
        Assert.True(viewModel.IsToggleOnlyVpnAfterFreezeWarningVisible);
        Assert.False(viewModel.IsTunguskaAutomationTokenVisible);
    }

    private static void SelectVpnAfterFreezeClient(
        DashboardWorkspaceViewModel viewModel,
        VpnAutomationClientKind kind)
    {
        var option = viewModel.VpnAfterFreezeClientOptions.Single(option => option.Kind == kind);
        option.SelectCommand.Execute(null);
    }

    private static bool IsVpnAfterFreezeClientSelected(
        DashboardWorkspaceViewModel viewModel,
        VpnAutomationClientKind kind)
    {
        return viewModel.VpnAfterFreezeClientOptions.Single(option => option.Kind == kind).IsSelected;
    }

    // Проверяет уведомление при недоступном ранее настроенном рабочем профиле.
    [Fact]
    public async Task EnsureInitializedAsync_shows_recovery_for_unavailable_work_profile()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(
                hasSetup: true,
                workProfileAvailable: false,
                workProfileState: WorkProfileStateKind.Unavailable,
                workProfileRecovery: WorkProfileRecoveryKind.DeleteWorkProfile,
                workProfileDiagnosticReason: "profile is unavailable")
        };
        var viewModel = TestWorkspaceFactory.Create(services);

        await viewModel.EnsureInitializedAsync();

        Assert.True(viewModel.HasSetup);
        Assert.False(viewModel.WorkProfileAvailable);
        Assert.Equal("Unavailable", viewModel.WorkProfileStatusText);
        Assert.True(viewModel.IsWorkProfileRecoveryVisible);
        Assert.False(viewModel.IsOnboardingVisible);
        Assert.Equal("Удалите рабочий профиль", viewModel.WorkProfileRecoveryTitle);
        Assert.Equal(
            "Этот профиль недоступен или не управляется Agnosia. Удалите его в настройках Android, затем вернитесь в Agnosia и создайте рабочий профиль заново.",
            viewModel.WorkProfileRecoveryMessage);
        Assert.False(viewModel.CanContinueOnboardingFromWorkProfile);
    }

    // Проверяет уведомление на первом запуске, если Android уже содержит чужой рабочий профиль.
    [Fact]
    public async Task EnsureInitializedAsync_shows_recovery_for_foreign_work_profile_on_first_launch()
    {
        var services = new TestPlatformServices
        {
            OnboardingCompleted = false,
            DashboardProfile = TestSnapshots.Dashboard(
                hasSetup: true,
                workProfileAvailable: false,
                workProfileState: WorkProfileStateKind.Unavailable,
                workProfileRecovery: WorkProfileRecoveryKind.DeleteWorkProfile,
                workProfileDiagnosticReason: "foreign profile")
        };
        var viewModel = TestWorkspaceFactory.Create(services);

        await viewModel.EnsureInitializedAsync();

        Assert.True(viewModel.IsOnboardingVisible);
        Assert.True(viewModel.IsWorkProfileRecoveryVisible);
        Assert.Equal("Удалите рабочий профиль", viewModel.WorkProfileRecoveryTitle);
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
        Assert.True(viewModel.IsOnboardingWorkProfileStep);
        Assert.Equal("2", viewModel.OnboardingStepLabel);
        Assert.False(viewModel.IsWorkProfileRecoveryVisible);
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

    // Проверяет, что действие recovery возвращает пользователя в начало онбординга.
    [Fact]
    public async Task RestartOnboardingFromWorkProfileRecoveryCommand_moves_unavailable_work_profile_to_onboarding_start()
    {
        var services = new TestPlatformServices
        {
            OnboardingCompleted = true,
            DashboardProfile = TestSnapshots.Dashboard(
                hasSetup: true,
                workProfileAvailable: false,
                workProfileState: WorkProfileStateKind.Unavailable,
                workProfileRecovery: WorkProfileRecoveryKind.DeleteWorkProfile),
            DefaultOperationResult = OperationResult.Failure("Android blocked provisioning")
        };
        var viewModel = TestWorkspaceFactory.Create(services);

        await viewModel.EnsureInitializedAsync();

        Assert.True(viewModel.IsWorkProfileRecoveryVisible);

        viewModel.RestartOnboardingFromWorkProfileRecoveryCommand.Execute(null);

        Assert.True(viewModel.IsOnboardingVisible);
        Assert.True(viewModel.IsOnboardingWelcomeStep);
        Assert.Equal("1", viewModel.OnboardingStepLabel);
        Assert.False(viewModel.IsWorkProfileRecoveryVisible);
        Assert.Equal(0, services.StartProvisioningCallCount);
    }

    // Проверяет, что на шаге рабочего профиля Agnosia не блокирует Android provisioning своим статусом.
    [Fact]
    public async Task StartProvisioningCommand_calls_android_from_onboarding_when_work_profile_is_unavailable()
    {
        var services = new TestPlatformServices
        {
            OnboardingCompleted = true,
            DashboardProfile = TestSnapshots.Dashboard(
                hasSetup: true,
                workProfileAvailable: false,
                workProfileState: WorkProfileStateKind.Unavailable,
                workProfileRecovery: WorkProfileRecoveryKind.DeleteWorkProfile),
            DefaultOperationResult = OperationResult.Failure("Android blocked provisioning")
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();
        viewModel.RestartOnboardingFromWorkProfileRecoveryCommand.Execute(null);
        viewModel.StartOnboardingCommand.Execute(null);

        Assert.True(viewModel.CanStartProvisioning);

        await viewModel.StartProvisioningCommand.ExecuteAsync(null);

        Assert.Equal(1, services.StartProvisioningCallCount);
        Assert.True(viewModel.StatusIsError);
        Assert.Equal("Android blocked provisioning", viewModel.StatusMessage);
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
