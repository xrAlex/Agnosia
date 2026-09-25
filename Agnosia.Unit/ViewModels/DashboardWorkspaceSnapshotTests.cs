using Agnosia.Models;
using Agnosia.Unit.TestDoubles;
using Agnosia.Unit.TestSupport;
using Xunit;

namespace Agnosia.Unit.ViewModels;

public sealed class DashboardWorkspaceSnapshotTests
{
    [Fact]
    public async Task Returning_to_apps_refreshes_lazy_icon_bindings_without_resetting_the_list()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            AppInventory = new DashboardAppInventorySnapshot([TestSnapshots.App(ProfileKind.Personal)], [])
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();
        await AsyncAssert.EventuallyAsync(() => viewModel.VisibleApps.Count == 1, "Initial inventory should load.");
        viewModel.OpenAppsSectionCommand.Execute(null);
        var visibleApps = viewModel.VisibleApps;
        var card = Assert.Single(visibleApps);
        viewModel.OpenOverviewSectionCommand.Execute(null);
        var changed = new List<string?>();
        card.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        viewModel.OpenAppsSectionCommand.Execute(null);

        Assert.Same(visibleApps, viewModel.VisibleApps);
        Assert.Contains(nameof(card.Icon), changed);
        Assert.Contains(nameof(card.HasIcon), changed);
        Assert.Contains(nameof(card.ShowMonogram), changed);
        Assert.Empty(services.AppIconLoadRequests);
    }

    [Fact]
    public async Task Inventory_refresh_updates_cards_without_resetting_unchanged_visible_items()
    {
        var app = TestSnapshots.App(ProfileKind.Personal, "test.catalog", "Before");
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            AppInventory = new DashboardAppInventorySnapshot([app], [])
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();
        await AsyncAssert.EventuallyAsync(() => viewModel.VisibleApps.Count == 1, "Initial inventory should load.");
        var visibleApps = viewModel.VisibleApps;
        var card = Assert.Single(visibleApps);
        var resetCount = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(viewModel.VisibleApps)) resetCount++;
        };
        services.AppInventory = new DashboardAppInventorySnapshot([app with { Label = "After" }], []);

        await viewModel.RefreshCommand.ExecuteAsync(null);
        await AsyncAssert.EventuallyAsync(() => card.Label == "After", "Card should receive the new snapshot.");

        Assert.Same(visibleApps, viewModel.VisibleApps);
        Assert.Equal(0, resetCount);
    }

    [Fact]
    public async Task Inventory_refresh_publishes_changed_order_and_removes_stale_cards()
    {
        var first = TestSnapshots.App(ProfileKind.Personal, "test.first", "First");
        var second = TestSnapshots.App(ProfileKind.Personal, "test.second", "Second");
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            AppInventory = new DashboardAppInventorySnapshot([first, second], [])
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();
        await AsyncAssert.EventuallyAsync(() => viewModel.VisibleApps.Count == 2, "Initial inventory should load.");
        var firstCard = viewModel.VisibleApps[0];
        var secondCard = viewModel.VisibleApps[1];
        services.AppInventory = new DashboardAppInventorySnapshot([second, first], []);

        await viewModel.RefreshCommand.ExecuteAsync(null);
        await AsyncAssert.EventuallyAsync(() => ReferenceEquals(viewModel.VisibleApps[0], secondCard), "New order should appear.");
        Assert.Same(firstCard, viewModel.VisibleApps[1]);

        services.AppInventory = new DashboardAppInventorySnapshot([second], []);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        await AsyncAssert.EventuallyAsync(() => viewModel.VisibleApps.Count == 1, "Removed card should disappear.");
        Assert.Same(secondCard, Assert.Single(viewModel.VisibleApps));
    }

    [Fact]
    public async Task Command_transport_selection_loads_and_switches_between_manual_and_auto()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(settings: AppSettingsSnapshot.Default with
            {
                CommandTransport = CommandTransportPreference.Activity
            })
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();

        Assert.True(viewModel.IsActivityTransportSelected);
        viewModel.SelectProviderTransportCommand.Execute(null);
        Assert.True(viewModel.IsProviderTransportSelected);
        viewModel.SelectAutoTransportCommand.Execute(null);
        Assert.True(viewModel.IsAutoTransportSelected);
    }

    [Fact]
    public async Task Concurrent_dashboard_refreshes_share_the_same_generation()
    {
        var services = new TestPlatformServices { DashboardProfile = TestSnapshots.Dashboard() };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        services.LoadModulesHandler = async _ => { entered.TrySetResult(); await release.Task; return []; };
        var first = viewModel.RefreshCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var duplicate = viewModel.RefreshCommand.ExecuteAsync(null);
        release.SetResult();
        await Task.WhenAll(first, duplicate);
        Assert.Equal(2, services.DashboardProfileLoadCount);
    }

    [Fact]
    public async Task Refresh_reports_updated_only_after_inventory_is_loaded()
    {
        var inventory = new TaskCompletionSource<DashboardAppInventorySnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            LoadAppInventoryHandler = (_, cancellation) => inventory.Task.WaitAsync(cancellation)
        };
        var viewModel = TestWorkspaceFactory.Create(services);

        await viewModel.EnsureInitializedAsync();

        Assert.Equal("LoadingApps", viewModel.StatusMessage);
        inventory.SetResult(new DashboardAppInventorySnapshot([], [
            TestSnapshots.App(ProfileKind.Work, "ru.fourpda.client", "4pda")
        ]));
        await AsyncAssert.EventuallyAsync(
            () => viewModel.WorkAppsCount == 1 && viewModel.StatusMessage == "Updated",
            "Refresh should report success only after the app inventory has been applied.");
        Assert.False(viewModel.StatusIsError);
    }

    [Fact]
    public async Task Failed_inventory_refresh_keeps_apps_and_never_reports_updated()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            AppInventory = new DashboardAppInventorySnapshot([], [
                TestSnapshots.App(ProfileKind.Work, "ru.fourpda.client", "4pda")
            ])
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();
        await AsyncAssert.EventuallyAsync(() => viewModel.WorkAppsCount == 1,
            "The initial work inventory should be available.");
        var inventory = new TaskCompletionSource<DashboardAppInventorySnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        services.LoadAppInventoryHandler = (_, cancellation) => inventory.Task.WaitAsync(cancellation);
        var statuses = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(viewModel.StatusMessage)) statuses.Add(viewModel.StatusMessage);
        };

        await viewModel.RefreshCommand.ExecuteAsync(null);
        inventory.SetException(new InvalidOperationException("Work inventory unavailable."));
        await AsyncAssert.EventuallyAsync(() => viewModel.StatusIsError,
            "The inventory failure should be reported.");

        Assert.Equal("LoadAppsFailed", viewModel.StatusMessage);
        Assert.Equal(1, viewModel.WorkAppsCount);
        Assert.DoesNotContain("Updated", statuses);
    }

    [Fact]
    public async Task Inventory_completion_preserves_a_newer_operation_status()
    {
        var inventory = new TaskCompletionSource<DashboardAppInventorySnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            LoadAppInventoryHandler = (_, cancellation) => inventory.Task.WaitAsync(cancellation)
        };
        var viewModel = TestWorkspaceFactory.Create(services);
        await viewModel.EnsureInitializedAsync();
        viewModel.StatusMessage = "CopyPersonalToWorkFinished";

        inventory.SetResult(new DashboardAppInventorySnapshot([], [
            TestSnapshots.App(ProfileKind.Work, "ru.fourpda.client", "4pda")
        ]));
        await AsyncAssert.EventuallyAsync(() => viewModel.WorkAppsCount == 1,
            "The inventory should be applied without replacing a newer status.");

        Assert.Equal("CopyPersonalToWorkFinished", viewModel.StatusMessage);
    }

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

    [Fact]
    public async Task Dashboard_snapshot_shows_update_failed_recovery_message()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(
                workProfileAvailable: false,
                workProfileState: WorkProfileStateKind.Unavailable,
                workProfileRecovery: WorkProfileRecoveryKind.UpdateFailedDeleteWorkProfile,
                workProfileDiagnosticReason: "profileUpdate=failed",
                workApps: [])
        };
        var viewModel = TestWorkspaceFactory.Create(services);

        await viewModel.EnsureInitializedAsync();

        Assert.True(viewModel.IsWorkProfileRecoveryVisible);
        Assert.Equal("Обновление не удалось", viewModel.WorkProfileRecoveryTitle);
        Assert.Equal("Обновление не удалось, удалите профиль.", viewModel.WorkProfileRecoveryMessage);
    }

    [Fact]
    public async Task Dashboard_snapshot_shows_probably_deleted_recovery_and_restarts_onboarding()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(
                workProfileAvailable: false,
                workProfileState: WorkProfileStateKind.Unavailable,
                workProfileRecovery: WorkProfileRecoveryKind.ProbablyDeletedRestartOnboarding,
                workProfileDiagnosticReason: "quietMode=True; userRunning=False")
        };
        var viewModel = TestWorkspaceFactory.Create(services);

        await viewModel.EnsureInitializedAsync();

        Assert.True(viewModel.IsWorkProfileRecoveryVisible);
        Assert.True(viewModel.IsWorkProfileRecoveryOnboardingRestart);
        Assert.Equal("Рабочий профиль удалён", viewModel.WorkProfileRecoveryTitle);

        viewModel.RestartOnboardingFromWorkProfileRecoveryCommand.Execute(null);

        Assert.True(viewModel.IsOnboardingVisible);
        Assert.False(viewModel.IsWorkProfileRecoveryVisible);
    }
}
