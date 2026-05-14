using Agnosia.ViewModels;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace Agnosia.Views;

public partial class LogOverlayView : UserControl
{
    public LogOverlayView()
    {
        InitializeComponent();
    }

    private async void OnCopyLogsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DashboardWorkspaceViewModel viewModel) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is null)
        {
            viewModel.StatusIsError = true;
            viewModel.StatusMessage = "Буфер обмена недоступен.";
            return;
        }

        try
        {
            await topLevel.Clipboard.SetTextAsync(viewModel.LogOutput);
            viewModel.StatusIsError = false;
            viewModel.StatusMessage = "Журнал скопирован в буфер обмена.";
        }
        catch (Exception)
        {
            viewModel.StatusIsError = true;
            viewModel.StatusMessage = "Не удалось скопировать журнал.";
        }
    }
}