using Agnosia.ViewModels;
using Avalonia.Controls;

namespace Agnosia.Views;

public partial class MainView : UserControl
{
    private bool _initialized;

    public MainView()
    {
        InitializeComponent();

        AttachedToVisualTree += async (_, _) =>
        {
            if (_initialized || DataContext is not DashboardWorkspaceViewModel viewModel) return;

            _initialized = true;
            await viewModel.EnsureInitializedAsync();
        };
    }
}