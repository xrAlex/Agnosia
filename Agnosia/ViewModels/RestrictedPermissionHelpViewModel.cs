using Agnosia.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Agnosia.ViewModels;

public sealed partial class RestrictedPermissionHelpViewModel(
    DashboardWorkspaceViewModel owner,
    PermissionKind? permission,
    bool isGranted,
    bool canRequest) : ObservableObject
{
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public bool IsVisible => !isGranted && permission is PermissionKind.UsageStats or PermissionKind.Overlay;

    public bool CanOpenSettings => IsVisible && canRequest;

    public ProfileKind Profile => permission == PermissionKind.UsageStats ? ProfileKind.Work : ProfileKind.Personal;

    public string ProfileLabel => Profile == ProfileKind.Work ? "Рабочий профиль" : "Основной профиль";

    [RelayCommand(CanExecute = nameof(CanOpenSettings))]
    private Task OpenSettingsAsync() => CanOpenSettings
        ? owner.OpenAppDetailsSettingsAsync(permission!.Value, Profile)
        : Task.CompletedTask;
}
