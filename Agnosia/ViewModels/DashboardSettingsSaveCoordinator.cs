using Agnosia.Models;
using Agnosia.Platform;
using Agnosia.Services;
using Avalonia.Threading;

namespace Agnosia.ViewModels;

internal sealed class DashboardSettingsSaveCoordinator
{
    private readonly ISettingsPlatformService _settingsService;
    private readonly DebouncedAsyncAction _saveDebouncer;
    private readonly Func<bool> _canQueue;
    private readonly Func<bool> _canProcess;
    private readonly Func<bool> _isAppsSectionSelected;
    private readonly Func<AppSettingsSnapshot> _captureSettings;
    private readonly Func<Task> _refreshAsync;
    private readonly Action<bool, string?> _setStatus;
    private readonly Func<Exception, string, string> _resolveExceptionMessage;
    private bool _isProcessing;
    private bool _hasPendingSave;
    private bool _hasPendingCatalogReload;
    private bool _loadedShowAllApps;
    private int _saveVersion;

    public DashboardSettingsSaveCoordinator(
        ISettingsPlatformService settingsService,
        TimeSpan saveDelay,
        Func<bool> canQueue,
        Func<bool> canProcess,
        Func<bool> isAppsSectionSelected,
        Func<AppSettingsSnapshot> captureSettings,
        Func<Task> refreshAsync,
        Action<bool, string?> setStatus,
        Func<Exception, string, string> resolveExceptionMessage,
        Func<Exception, string, Task> reportErrorOnUiThreadAsync,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _canQueue = canQueue ?? throw new ArgumentNullException(nameof(canQueue));
        _canProcess = canProcess ?? throw new ArgumentNullException(nameof(canProcess));
        _isAppsSectionSelected =
            isAppsSectionSelected ?? throw new ArgumentNullException(nameof(isAppsSectionSelected));
        _captureSettings = captureSettings ?? throw new ArgumentNullException(nameof(captureSettings));
        _refreshAsync = refreshAsync ?? throw new ArgumentNullException(nameof(refreshAsync));
        _setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));
        _resolveExceptionMessage =
            resolveExceptionMessage ?? throw new ArgumentNullException(nameof(resolveExceptionMessage));
        _saveDebouncer = new DebouncedAsyncAction(
            saveDelay,
            exception => reportErrorOnUiThreadAsync(exception, "SettingsScheduleFailed"),
            delayAsync ?? Task.Delay);
    }

    public void SetLoadedShowAllApps(bool showAllApps)
    {
        _loadedShowAllApps = showAllApps;
        _hasPendingCatalogReload = false;
    }

    public void Queue()
    {
        if (!_canQueue()) return;

        _saveVersion++;
        _hasPendingSave = true;
        Schedule();
    }

    private void CancelPending()
    {
        _saveDebouncer.Cancel();
    }

    public void TryStartQueued()
    {
        if (_isProcessing || !_canProcess() || !_hasPendingSave) return;

        CancelPending();
        _ = ProcessQueuedSavesAsync();
    }

    public void TryStartPendingCatalogRefresh()
    {
        if (!_hasPendingCatalogReload
            || !_isAppsSectionSelected()
            || _isProcessing
            || !_canProcess())
            return;

        _ = _refreshAsync();
    }

    private void Schedule()
    {
        CancelPending();
        _saveDebouncer.Schedule(TryStartQueuedAfterDelayAsync);
    }

    private async Task TryStartQueuedAfterDelayAsync(CancellationToken cancellationToken)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!cancellationToken.IsCancellationRequested) TryStartQueued();
        }, DispatcherPriority.Background);
    }

    private async Task ProcessQueuedSavesAsync()
    {
        if (_isProcessing) return;

        _isProcessing = true;

        try
        {
            while (_hasPendingSave && _canProcess())
            {
                _hasPendingSave = false;
                var saveVersion = _saveVersion;
                var settings = _captureSettings();
                _setStatus(false, null);

                try
                {
                    var result = await _settingsService.SaveSettingsAsync(settings);
                    _setStatus(
                        !result.Succeeded,
                        result.Succeeded
                            ? null
                            : string.IsNullOrWhiteSpace(result.Message)
                                ? "SettingsApplyFailed"
                                : result.Message);

                    if (result.Succeeded && saveVersion == _saveVersion)
                    {
                        _hasPendingCatalogReload = settings.ShowAllApps != _loadedShowAllApps;
                        TryStartPendingCatalogRefresh();
                    }
                }
                catch (Exception exception)
                {
                    _setStatus(true, _resolveExceptionMessage(exception, "SettingsSaveFailed"));
                }
            }
        }
        catch (Exception exception)
        {
            _setStatus(true, _resolveExceptionMessage(exception, "SettingsQueueFailed"));
        }
        finally
        {
            _isProcessing = false;
            if (_hasPendingSave)
                TryStartQueued();
            else
                TryStartPendingCatalogRefresh();
        }
    }
}
