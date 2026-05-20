using Agnosia.Models;
using Agnosia.Unit.TestDoubles;
using Agnosia.Unit.TestSupport;
using Agnosia.ViewModels;
using Xunit;

namespace Agnosia.Unit.ViewModels;

public sealed class DashboardSettingsSaveCoordinatorTests
{
    // Проверяет, что сохранение не ставится в очередь, когда queue запрещен.
    [Fact]
    public void Queue_ignores_request_when_queueing_is_disabled()
    {
        var services = new TestPlatformServices();
        var coordinator = CreateCoordinator(
            services,
            canQueue: () => false);

        coordinator.Queue();
        coordinator.TryStartQueued();

        Assert.Empty(services.SavedSettings);
    }

    // Проверяет сохранение текущих настроек и сброс статуса ошибки.
    [Fact]
    public async Task TryStartQueued_saves_captured_settings_and_clears_status()
    {
        var services = new TestPlatformServices();
        var statuses = new List<(bool IsError, string? Message)>();
        var settings = AppSettingsSnapshot.Default with { BlockContactsSearching = false };
        var coordinator = CreateCoordinator(
            services,
            captureSettings: () => settings,
            setStatus: statuses.Add);

        coordinator.Queue();
        coordinator.TryStartQueued();

        await AsyncAssert.EventuallyAsync(
            () => services.SavedSettings.Count == 1,
            "Queued settings should be saved once.");

        Assert.Equal(settings, services.SavedSettings[0]);
        Assert.Contains((false, null), statuses);
    }

    // Проверяет refresh каталога после успешного изменения ShowAllApps в разделе приложений.
    [Fact]
    public async Task Successful_save_refreshes_catalog_when_show_all_apps_changed_and_apps_section_is_selected()
    {
        var services = new TestPlatformServices();
        var refreshCount = 0;
        var coordinator = CreateCoordinator(
            services,
            isAppsSectionSelected: () => true,
            captureSettings: () => AppSettingsSnapshot.Default with { ShowAllApps = true },
            refreshAsync: () =>
            {
                refreshCount++;
                return Task.CompletedTask;
            });
        coordinator.SetLoadedShowAllApps(false);

        coordinator.Queue();
        coordinator.TryStartQueued();

        await AsyncAssert.EventuallyAsync(
            () => refreshCount == 1,
            "Catalog should refresh after a successful ShowAllApps change while apps section is selected.");
    }

    // Проверяет отложенный refresh каталога до открытия раздела приложений.
    [Fact]
    public async Task Successful_save_defers_catalog_refresh_until_apps_section_is_selected()
    {
        var services = new TestPlatformServices();
        var appsSectionSelected = false;
        var refreshCount = 0;
        var coordinator = CreateCoordinator(
            services,
            isAppsSectionSelected: () => appsSectionSelected,
            captureSettings: () => AppSettingsSnapshot.Default with { ShowAllApps = true },
            refreshAsync: () =>
            {
                refreshCount++;
                return Task.CompletedTask;
            });
        coordinator.SetLoadedShowAllApps(false);

        coordinator.Queue();
        coordinator.TryStartQueued();

        await AsyncAssert.EventuallyAsync(
            () => services.SavedSettings.Count == 1,
            "Queued settings should be saved before catalog refresh is considered.");
        Assert.Equal(0, refreshCount);

        appsSectionSelected = true;
        coordinator.TryStartPendingCatalogRefresh();

        Assert.Equal(1, refreshCount);
    }

    // Проверяет ошибочный результат сохранения и отсутствие refresh каталога.
    [Fact]
    public async Task Failed_save_sets_error_status_and_skips_catalog_refresh()
    {
        var services = new TestPlatformServices
        {
            DefaultOperationResult = OperationResult.Failure("Settings rejected")
        };
        var statuses = new List<(bool IsError, string? Message)>();
        var refreshCount = 0;
        var coordinator = CreateCoordinator(
            services,
            isAppsSectionSelected: () => true,
            captureSettings: () => AppSettingsSnapshot.Default with { ShowAllApps = true },
            refreshAsync: () =>
            {
                refreshCount++;
                return Task.CompletedTask;
            },
            setStatus: statuses.Add);
        coordinator.SetLoadedShowAllApps(false);

        coordinator.Queue();
        coordinator.TryStartQueued();

        await AsyncAssert.EventuallyAsync(
            () => statuses.Contains((true, "Settings rejected")),
            "Failed save should publish the platform error.");

        Assert.Equal(0, refreshCount);
    }

    // Проверяет преобразование исключения сохранения через resolver fallback-сообщения.
    [Fact]
    public async Task Save_exception_uses_resolver_fallback_message()
    {
        var services = new TestPlatformServices
        {
            SaveSettingsHandler = (_, _) => throw new InvalidOperationException("storage unavailable")
        };
        var statuses = new List<(bool IsError, string? Message)>();
        var coordinator = CreateCoordinator(
            services,
            setStatus: statuses.Add,
            resolveExceptionMessage: (exception, fallback) => $"{fallback}:{exception.GetType().Name}");

        coordinator.Queue();
        coordinator.TryStartQueued();

        await AsyncAssert.EventuallyAsync(
            () => statuses.Contains((true, "SettingsSaveFailed:InvalidOperationException")),
            "Save exceptions should be translated through the provided resolver.");
    }

    // Проверяет coalescing: несколько Queue до обработки сохраняют только последний snapshot.
    [Fact]
    public async Task Multiple_queued_saves_before_processing_save_latest_snapshot()
    {
        var services = new TestPlatformServices();
        var settings = AppSettingsSnapshot.Default;
        var coordinator = CreateCoordinator(
            services,
            captureSettings: () => settings);

        settings = AppSettingsSnapshot.Default with { BlockContactsSearching = false };
        coordinator.Queue();
        settings = AppSettingsSnapshot.Default with { DisableVpnBeforeWorkLaunch = true };
        coordinator.Queue();
        settings = AppSettingsSnapshot.Default with { EnableVpnAfterWorkFreeze = true };
        coordinator.Queue();
        coordinator.TryStartQueued();

        await AsyncAssert.EventuallyAsync(
            () => services.SavedSettings.Count == 1,
            "Coalesced settings queue should save once.");

        Assert.Equal(settings, services.SavedSettings[0]);
    }

    // Проверяет, что Queue во время активного сохранения запускает второй проход.
    [Fact]
    public async Task Queue_during_processing_runs_second_save_pass()
    {
        var services = new TestPlatformServices();
        var firstSaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        services.SaveSettingsHandler = async (_, cancellationToken) =>
        {
            firstSaveStarted.TrySetResult();
            await releaseSave.Task.WaitAsync(cancellationToken);
            return OperationResult.Success("Ok");
        };
        var settings = AppSettingsSnapshot.Default with { BlockContactsSearching = false };
        var coordinator = CreateCoordinator(
            services,
            captureSettings: () => settings);

        coordinator.Queue();
        coordinator.TryStartQueued();
        await firstSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        settings = AppSettingsSnapshot.Default with { DisableVpnBeforeWorkLaunch = true };
        coordinator.Queue();
        releaseSave.SetResult();

        await AsyncAssert.EventuallyAsync(
            () => services.SavedSettings.Count == 2,
            "Queueing during processing should produce a second save pass.");

        Assert.Equal(
            AppSettingsSnapshot.Default with { BlockContactsSearching = false },
            services.SavedSettings[0]);
        Assert.Equal(settings, services.SavedSettings[1]);
    }

    // Проверяет, что processing guard удерживает save до явного TryStartQueued после разблокировки.
    [Fact]
    public async Task Queued_save_waits_until_can_process_becomes_true()
    {
        var services = new TestPlatformServices();
        var canProcess = false;
        var coordinator = CreateCoordinator(
            services,
            canProcess: () => canProcess);

        coordinator.Queue();
        coordinator.TryStartQueued();

        Assert.Empty(services.SavedSettings);

        canProcess = true;
        coordinator.TryStartQueued();

        await AsyncAssert.EventuallyAsync(
            () => services.SavedSettings.Count == 1,
            "Queued save should start after processing becomes available.");
    }

    private static DashboardSettingsSaveCoordinator CreateCoordinator(
        TestPlatformServices settingsService,
        Func<bool>? canQueue = null,
        Func<bool>? canProcess = null,
        Func<bool>? isAppsSectionSelected = null,
        Func<AppSettingsSnapshot>? captureSettings = null,
        Func<Task>? refreshAsync = null,
        Action<(bool IsError, string? Message)>? setStatus = null,
        Func<Exception, string, string>? resolveExceptionMessage = null)
    {
        return new DashboardSettingsSaveCoordinator(
            settingsService,
            TimeSpan.FromDays(1),
            canQueue ?? (() => true),
            canProcess ?? (() => true),
            isAppsSectionSelected ?? (() => false),
            captureSettings ?? (() => AppSettingsSnapshot.Default),
            refreshAsync ?? (() => Task.CompletedTask),
            (isError, message) => setStatus?.Invoke((isError, message)),
            resolveExceptionMessage ?? ((_, fallback) => fallback),
            (_, _) => Task.CompletedTask);
    }
}
