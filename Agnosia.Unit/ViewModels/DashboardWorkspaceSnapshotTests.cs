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
            workProfileState: WorkProfileStateKind.WorkProfileUnavailable);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsPersonalProfileSelected);
        Assert.False(viewModel.IsWorkProfileSelected);
    }

    // Проверяет сохранение dismissed recovery до смены типа recovery.
    [Fact]
    public async Task Dashboard_snapshot_keeps_dismissed_recovery_until_recovery_kind_changes()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(
                workProfileAvailable: false,
                workProfileState: WorkProfileStateKind.WorkProfileQuietMode,
                workProfileRecovery: WorkProfileRecoveryKind.WorkProfileQuietMode,
                workProfileDiagnosticReason: "quiet")
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();
        Assert.True(viewModel.IsWorkProfileRecoveryVisible);

        viewModel.DismissWorkProfileRecoveryCommand.Execute(null);
        Assert.True(viewModel.WorkProfileRecoveryDismissed);
        Assert.False(viewModel.IsWorkProfileRecoveryVisible);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(viewModel.WorkProfileRecoveryDismissed);
        Assert.False(viewModel.IsWorkProfileRecoveryVisible);

        services.DashboardProfile = TestSnapshots.Dashboard(
            workProfileAvailable: false,
            workProfileState: WorkProfileStateKind.WorkProfileUnavailable,
            workProfileRecovery: WorkProfileRecoveryKind.WorkProfileUnavailable,
            workProfileDiagnosticReason: "unavailable");

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(viewModel.WorkProfileRecoveryDismissed);
        Assert.True(viewModel.IsWorkProfileRecoveryVisible);
        Assert.Equal("Рабочий профиль недоступен", viewModel.WorkProfileRecoveryTitle);
    }
}
