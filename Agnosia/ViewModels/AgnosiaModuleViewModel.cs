using System.Collections.ObjectModel;
using Agnosia.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Agnosia.ViewModels;

public sealed partial class AgnosiaModuleViewModel : ObservableObject
{
    private readonly DashboardWorkspaceViewModel _owner;
    private readonly ObservableCollection<AgnosiaModuleRequirementViewModel> _requirements = [];

    public AgnosiaModuleViewModel(DashboardWorkspaceViewModel owner, AgnosiaModuleSnapshot snapshot)
    {
        _owner = owner;
        Requirements = new ReadOnlyObservableCollection<AgnosiaModuleRequirementViewModel>(_requirements);
        ApplySnapshot(snapshot);
    }

    public AgnosiaModuleKind Kind { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string ShortDescription { get; private set; } = string.Empty;

    public string FullDescription { get; private set; } = string.Empty;

    public bool IsEnabled { get; private set; }

    public AgnosiaModuleState State { get; private set; }

    public string StatusText { get; private set; } = string.Empty;

    public bool CanToggle { get; private set; }

    public bool IsFileShuttle => Kind == AgnosiaModuleKind.FileShuttle;

    public bool IsVpnGuard => Kind == AgnosiaModuleKind.VpnGuard;

    public bool IsRiskEngine => Kind == AgnosiaModuleKind.RiskEngine;

    public bool CanOpenDocumentsUi => IsFileShuttle && State == AgnosiaModuleState.Enabled;

    public bool HasMissingRequirements => _requirements.Any(requirement => !requirement.IsSatisfied);

    public IReadOnlyList<VpnAutomationClientOptionViewModel> VpnAfterFreezeClientOptions =>
        _owner.VpnAfterFreezeClientOptions;

    public bool IsToggleOnlyVpnAfterFreezeWarningVisible => _owner.IsToggleOnlyVpnAfterFreezeWarningVisible;

    public bool IsTunguskaAutomationTokenVisible => _owner.IsTunguskaAutomationTokenVisible;

    public string TunguskaAutomationToken
    {
        get => _owner.TunguskaAutomationToken;
        set => _owner.TunguskaAutomationToken = value;
    }

    public ReadOnlyObservableCollection<AgnosiaModuleRequirementViewModel> Requirements { get; }

    public void ApplySnapshot(AgnosiaModuleSnapshot snapshot)
    {
        Kind = snapshot.Kind;
        Title = snapshot.Title;
        ShortDescription = snapshot.ShortDescription;
        FullDescription = snapshot.FullDescription;
        IsEnabled = snapshot.IsEnabled;
        State = snapshot.State;
        StatusText = snapshot.StatusText;
        CanToggle = snapshot.CanSetEnabled;

        _requirements.Clear();
        foreach (var requirement in snapshot.Requirements)
            _requirements.Add(new AgnosiaModuleRequirementViewModel(_owner, requirement));

        OnPropertyChanged(string.Empty);
        ToggleEnabledCommand.NotifyCanExecuteChanged();
        OpenDocumentsUiCommand.NotifyCanExecuteChanged();
    }

    internal void NotifyVpnSettingsChanged()
    {
        if (!IsVpnGuard) return;

        OnPropertyChanged(nameof(IsToggleOnlyVpnAfterFreezeWarningVisible));
        OnPropertyChanged(nameof(IsTunguskaAutomationTokenVisible));
        OnPropertyChanged(nameof(TunguskaAutomationToken));
    }

    [RelayCommand]
    private void Open()
    {
        _owner.OpenModuleDetails(this);
    }

    [RelayCommand]
    private void Close()
    {
        _owner.CloseModuleDetailsCore();
    }

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private Task ToggleEnabledAsync()
    {
        if (!CanToggle) return Task.CompletedTask;

        return _owner.SetModuleEnabledAsync(this, !IsEnabled);
    }

    [RelayCommand(CanExecute = nameof(CanOpenDocumentsUi))]
    private Task OpenDocumentsUiAsync()
    {
        if (!CanOpenDocumentsUi) return Task.CompletedTask;

        return _owner.OpenDocumentsUiFromModuleAsync(this);
    }
}
