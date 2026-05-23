using Agnosia.Models;
using Agnosia.Unit.TestDoubles;
using Agnosia.Unit.TestSupport;
using Xunit;

namespace Agnosia.Unit.ViewModels;

public sealed class DashboardWorkspaceSnapshotTests
{
    // Проверяет обновление счетчиков приложений при применении snapshot каталога.
    [Fact]
    public async Task Dashboard_snapshot_updates_app_counts()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            AppInventory = new DashboardAppInventorySnapshot(
                [
                    TestSnapshots.App(ProfileKind.Personal, "com.example.alpha", "Alpha"),
                    TestSnapshots.App(ProfileKind.Personal, "com.example.beta", "Beta")
                ],
                [
                    TestSnapshots.App(ProfileKind.Work, "com.example.work", "Work")
                ])
        };
        var viewModel = TestWorkspaceFactory.Create(services);

        await viewModel.EnsureInitializedAsync();

        await AsyncAssert.EventuallyAsync(
            () => viewModel.PersonalAppsCount == 2 && viewModel.WorkAppsCount == 1,
            "Inventory snapshot should be applied to dashboard counters.");
        Assert.Equal(3, viewModel.TotalManagedAppsCount);
    }

    // Проверяет возврат выбранного Work профиля на Personal, если рабочий профиль стал недоступен.
    [Fact]
    public async Task Dashboard_snapshot_resets_selected_work_profile_when_work_profile_becomes_unavailable()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(workProfileAvailable: true)
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();
        viewModel.SelectWorkCommand.Execute(null);
        Assert.True(viewModel.IsWorkProfileSelected);

        services.DashboardProfile = TestSnapshots.Dashboard(
            workProfileAvailable: false,
            workProfileState: WorkProfileStateKind.Unavailable);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsPersonalProfileSelected);
        Assert.False(viewModel.IsWorkProfileSelected);
    }

    // Проверяет уведомление при повторном появлении недоступного рабочего профиля.
    [Fact]
    public async Task Dashboard_snapshot_shows_recovery_when_unavailable_profile_reappears()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(
                workProfileAvailable: false,
                workProfileState: WorkProfileStateKind.Unavailable,
                workProfileRecovery: WorkProfileRecoveryKind.DeleteWorkProfile,
                workProfileDiagnosticReason: "unavailable")
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();
        Assert.False(viewModel.WorkProfileRecoveryDismissed);
        Assert.True(viewModel.IsWorkProfileRecoveryVisible);
        Assert.False(viewModel.IsOnboardingVisible);

        viewModel.DismissWorkProfileRecoveryCommand.Execute(null);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(viewModel.WorkProfileRecoveryDismissed);
        Assert.False(viewModel.IsWorkProfileRecoveryVisible);

        services.DashboardProfile = TestSnapshots.Dashboard(
            workProfileAvailable: true,
            workProfileState: WorkProfileStateKind.Available);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(viewModel.WorkProfileRecoveryDismissed);
        Assert.False(viewModel.IsWorkProfileRecoveryVisible);
        Assert.False(viewModel.IsOnboardingVisible);

        services.DashboardProfile = TestSnapshots.Dashboard(
            workProfileAvailable: false,
            workProfileState: WorkProfileStateKind.Unavailable,
            workProfileRecovery: WorkProfileRecoveryKind.DeleteWorkProfile,
            workProfileDiagnosticReason: "unavailable-again");

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(viewModel.WorkProfileRecoveryDismissed);
        Assert.True(viewModel.IsWorkProfileRecoveryVisible);
        Assert.False(viewModel.IsOnboardingVisible);
        Assert.Equal("Удалите рабочий профиль", viewModel.WorkProfileRecoveryTitle);
    }

    // Проверяет, что recovery показывает простое действие без диагностического дампа Android.
    [Fact]
    public async Task Dashboard_snapshot_shows_simplified_recovery_message_without_diagnostics()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(
                workProfileAvailable: false,
                workProfileState: WorkProfileStateKind.Unavailable,
                workProfileRecovery: WorkProfileRecoveryKind.DeleteWorkProfile,
                workProfileDiagnosticReason: "state=Unavailable; ownerCheck=Unreachable")
        };
        var viewModel = TestWorkspaceFactory.Create(services);

        await viewModel.EnsureInitializedAsync();

        Assert.True(viewModel.IsWorkProfileRecoveryVisible);
        Assert.Equal("Удалите рабочий профиль", viewModel.WorkProfileRecoveryTitle);
        Assert.Equal(
            "Этот профиль недоступен или не управляется Agnosia. Удалите его в настройках Android, затем вернитесь в Agnosia и создайте рабочий профиль заново.",
            viewModel.WorkProfileRecoveryMessage);
    }
}
