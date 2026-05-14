using Agnosia.ViewModels;
using Avalonia;
using Avalonia.Controls;

namespace Agnosia.Views;

public partial class PermissionListView : UserControl
{
    public static readonly StyledProperty<IEnumerable<PermissionItemViewModel>?> ItemsProperty =
        AvaloniaProperty.Register<PermissionListView, IEnumerable<PermissionItemViewModel>?>(nameof(Items));

    public IEnumerable<PermissionItemViewModel>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public PermissionListView()
    {
        InitializeComponent();
    }
}