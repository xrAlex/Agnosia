using Agnosia.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;

namespace Agnosia.Views;

public partial class AppsSectionView : UserControl
{
    public AppsSectionView()
    {
        InitializeComponent();
    }

    private void OnAppCardTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: AppItemViewModel app }) return;

        if (!app.OpenControlsCommand.CanExecute(null)) return;
        app.OpenControlsCommand.Execute(null);
        e.Handled = true;
    }
}
