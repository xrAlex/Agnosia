using Agnosia.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Agnosia.ViewModels;

public sealed partial class AgnosiaModuleRequirementViewModel(
    DashboardWorkspaceViewModel owner,
    AgnosiaModuleRequirement snapshot) : ObservableObject
{
    public string Title => snapshot.Title;

    public string Description => snapshot.Description;

    public bool IsSatisfied => snapshot.IsSatisfied;

    public PermissionKind? PermissionKind => snapshot.PermissionKind;

    public string StatusLabel => IsSatisfied ? "Готово" : "Нужно действие";

    public string ActionLabel =>
        string.IsNullOrWhiteSpace(snapshot.ActionLabel) ? "Открыть" : snapshot.ActionLabel;

    public bool CanRequest => !IsSatisfied && PermissionKind.HasValue;

    [RelayCommand(CanExecute = nameof(CanRequest))]
    private Task RequestAsync()
    {
        return owner.RequestModuleRequirementAsync(this);
    }
}
