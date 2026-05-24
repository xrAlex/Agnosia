using Agnosia.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Agnosia.ViewModels;

public sealed partial class VpnAutomationClientOptionViewModel : ObservableObject
{
    private readonly DashboardWorkspaceViewModel _owner;

    internal VpnAutomationClientOptionViewModel(
        DashboardWorkspaceViewModel owner,
        VpnAutomationClientKind kind,
        string displayName)
    {
        _owner = owner;
        Kind = kind;
        DisplayName = displayName;
    }

    public VpnAutomationClientKind Kind { get; }

    public string DisplayName { get; }

    public bool IsSelected => _owner.IsVpnAfterFreezeClientSelected(Kind);

    [RelayCommand]
    private void Select()
    {
        _owner.SelectVpnAfterFreezeClient(Kind);
    }

    internal void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(IsSelected));
    }
}
