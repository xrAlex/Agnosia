using Agnosia.Models;
using CommunityToolkit.Mvvm.Input;

namespace Agnosia.ViewModels;

public sealed partial class PermissionItemViewModel
{
    private readonly DashboardWorkspaceViewModel _owner;

    public PermissionItemViewModel(DashboardWorkspaceViewModel owner, PermissionSnapshot snapshot)
    {
        _owner = owner;
        Snapshot = snapshot;
    }

    private PermissionSnapshot Snapshot { get; }

    public PermissionKind Kind => Snapshot.Kind;

    public string Title => Snapshot.Title;

    public string ProfileLabel => Snapshot.ProfileLabel;

    public string Description => Snapshot.Description;

    public bool IsGranted => Snapshot.IsGranted;

    public bool CanRequest => Snapshot.CanRequest && !Snapshot.IsGranted;

    public string StatusLabel => Snapshot.IsGranted ? Snapshot.GrantedLabel : "ActionRequired";

    public string RequestLabel => Snapshot.IsGranted ? Snapshot.GrantedLabel : Snapshot.RequestLabel;

    [RelayCommand(CanExecute = nameof(CanRequest))]
    private Task RequestAsync() => _owner.RequestPermissionAsync(this);
}
