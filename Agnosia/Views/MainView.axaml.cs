using Agnosia.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Agnosia.Views;

public partial class MainView : UserControl
{
    private bool _initialized;
    private TopLevel? _topLevel;

    public MainView()
    {
        InitializeComponent();

        AttachedToVisualTree += async (_, _) =>
        {
            _topLevel = TopLevel.GetTopLevel(this);
            if (_topLevel is not null) _topLevel.BackRequested += OnBackRequested;

            if (_initialized || DataContext is not DashboardWorkspaceViewModel viewModel) return;

            _initialized = true;
            await viewModel.EnsureInitializedAsync();
        };

        DetachedFromVisualTree += (_, _) =>
        {
            if (_topLevel is not null) _topLevel.BackRequested -= OnBackRequested;
            _topLevel = null;
        };
    }

    private void OnBackRequested(object? sender, RoutedEventArgs args)
    {
        if (!args.Handled && DataContext is DashboardWorkspaceViewModel viewModel)
            args.Handled = viewModel.TryHandleBack();
    }
}
