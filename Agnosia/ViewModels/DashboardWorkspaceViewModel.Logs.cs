using Agnosia.Models;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace Agnosia.ViewModels;

public partial class DashboardWorkspaceViewModel
{
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
    private void ClearLogs()
    {
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
        var shouldLoad = await InvokeOnUiThreadFuncAsync(
                () => LoggingEnabled && (force || IsLogWindowOpen),
                DispatcherPriority.Background)
            .ConfigureAwait(false);
        if (!shouldLoad) return;

        var logs = await LoadRecentLogsOnWorkerAsync().ConfigureAwait(false);
        await InvokeOnUiThreadActionAsync(
                () => ImportPlatformLogs(logs),
                DispatcherPriority.Background)
            .ConfigureAwait(false);
    }

    private void ImportPlatformLogs(IEnumerable<AppLogEntry> logs)
    {
        if (_eventLogService.ImportPlatformLogs(logs)) NotifyLogStateChanged();
    }
}
