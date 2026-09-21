using Agnosia.Models;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace Agnosia.ViewModels;

public partial class DashboardWorkspaceViewModel
{
    private int _logGeneration;
    private bool _clearingLogs;

    [RelayCommand]
    private async Task OpenLogsAsync()
    {
        if (!LoggingEnabled)
            return;

        await ReloadPlatformLogsAsync(true);
        IsLogWindowOpen = true;
    }

    [RelayCommand]
    private void CloseLogs() => IsLogWindowOpen = false;

    [RelayCommand]
    private async Task ClearLogsAsync()
    {
        _clearingLogs = true;
        ClearDisplayedLogs();
        try
        {
            var result = await Task.Run(() => _platformEventLogReader.ClearRecentLogsAsync());
            if (!result.Succeeded) StatusMessage = result.Message;
        }
        catch (Exception)
        {
            StatusMessage = "Не удалось полностью очистить журнал. Повторите попытку, когда рабочий профиль доступен.";
        }
        finally
        {
            ClearDisplayedLogs();
            _clearingLogs = false;
        }
    }

    private void ClearDisplayedLogs()
    {
        _logGeneration++;
        _eventLogService.Clear();
        NotifyLogStateChanged();
    }

    private void NotifyLogStateChanged()
    {
        OnPropertyChanged(nameof(LogSummary));
        OnPropertyChanged(nameof(LogOutput));
        OnPropertyChanged(nameof(LogLines));
    }

    private async Task ReloadPlatformLogsAsync(bool force = false)
    {
        var generation = await InvokeOnUiThreadFuncAsync(
                () => LoggingEnabled && !_clearingLogs && (force || IsLogWindowOpen) ? _logGeneration : -1,
                DispatcherPriority.Background)
            .ConfigureAwait(false);
        if (generation < 0) return;

        var logs = await LoadRecentLogsOnWorkerAsync().ConfigureAwait(false);
        await InvokeOnUiThreadActionAsync(
                () => { if (!_clearingLogs && generation == _logGeneration) ImportPlatformLogs(logs); },
                DispatcherPriority.Background)
            .ConfigureAwait(false);
    }

    private void ImportPlatformLogs(IEnumerable<AppLogEntry> logs)
    {
        if (_eventLogService.ImportPlatformLogs(logs)) NotifyLogStateChanged();
    }
}
