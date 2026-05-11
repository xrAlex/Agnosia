using Agnosia.Models;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Agnosia.ViewModels;

public partial class AppItemViewModel : ObservableObject, IDisposable
{
    private readonly DashboardWorkspaceViewModel _owner;
    private bool _iconLoadRequested;
    private bool _disposed;
    private Bitmap? _icon;

    public AppItemViewModel(DashboardWorkspaceViewModel owner, AppSnapshot snapshot)
    {
        _owner = owner;
        Snapshot = snapshot;
    }

    public AppSnapshot Snapshot { get; }

    public string PackageName => Snapshot.PackageName;

    public string Label => Snapshot.Label;

    public ProfileKind Profile => Snapshot.Profile;

    public bool IsHidden => Snapshot.IsHidden;

    public bool InteractionAllowed => Snapshot.InteractionAllowed;

    public string Monogram => string.IsNullOrWhiteSpace(Snapshot.Label)
        ? "?"
        : char.ToUpperInvariant(Snapshot.Label[0]).ToString();

    public Bitmap? Icon
    {
        get
        {
            RequestIconLoad();
            return _icon;
        }

        private set
        {
            if (ReferenceEquals(_icon, value))
            {
                return;
            }

            var previousIcon = _icon;
            if (SetProperty(ref _icon, value))
            {
                previousIcon?.Dispose();
            }
        }
    }

    public bool HasIcon
    {
        get
        {
            RequestIconLoad();
            return _icon is not null;
        }
    }

    public bool ShowMonogram
    {
        get
        {
            RequestIconLoad();
            return _icon is null;
        }
    }

    public string StatusTagLabel => ResolveStatusTagLabel(Snapshot);

    public string ProfileLabel => Profile == ProfileKind.Work ? "Work" : "Personal";

    public bool HasStatusTag => StatusTagLabel.Length > 0;

    public bool ShowSecondaryRow => HasStatusTag || Profile == ProfileKind.Work;

    public bool ShowWorkControls => Profile == ProfileKind.Work;

    public bool CanClone => Profile == ProfileKind.Personal || !Snapshot.IsSystem;

    public bool CanMoveToWork => Profile == ProfileKind.Personal && CanClone && CanUninstall;

    public bool CanUninstall => !Snapshot.IsSystem;

    public bool CanFreeze => Profile == ProfileKind.Work;

    public bool ShowLaunch => Snapshot.CanLaunch || Profile == ProfileKind.Work;

    public string LaunchLabel => Profile == ProfileKind.Work && IsHidden ? "UnfreezeAndOpen" : "Open";

    public string CloneLabel => Profile == ProfileKind.Work ? "CopyToPersonal" : "CopyToWork";

    public string MoveToWorkLabel => "MoveToWork";

    public string FreezeLabel => IsHidden ? "Unfreeze" : "Freeze";

    public string ForceFreezeLabel => "ForceFreeze";

    public string CreateShortcutLabel => "CreateShortcut";

    public string UninstallLabel => "Uninstall";

    public string InteractionLabel => InteractionAllowed ? "DisallowInteraction" : "AllowInteraction";

    [RelayCommand(CanExecute = nameof(CanClone))]
    private Task CloneAsync() => _owner.CloneAsync(this);

    [RelayCommand(CanExecute = nameof(CanMoveToWork))]
    private Task MoveToWorkAsync() => _owner.MoveToWorkAsync(this);

    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private Task UninstallAsync() => _owner.UninstallAsync(this);

    [RelayCommand(CanExecute = nameof(CanFreeze))]
    private Task ToggleFrozenAsync() => _owner.ToggleFrozenAsync(this);

    [RelayCommand(CanExecute = nameof(CanFreeze))]
    private Task ForceFreezeAsync() => _owner.ForceFreezeAsync(this);

    [RelayCommand(CanExecute = nameof(ShowWorkControls))]
    private Task CreateShortcutAsync() => _owner.CreateShortcutAsync(this);

    [RelayCommand(CanExecute = nameof(ShowLaunch))]
    private Task LaunchAsync() => _owner.LaunchAsync(this);

    [RelayCommand(CanExecute = nameof(ShowWorkControls))]
    private Task ToggleInteractionAccessAsync() => _owner.ToggleInteractionAccessAsync(this);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon?.Dispose();
        _icon = null;
    }

    private void RequestIconLoad()
    {
        if (_disposed || _iconLoadRequested || Snapshot.IconPng is not { Length: > 0 } iconPng)
        {
            return;
        }

        _iconLoadRequested = true;
        _ = LoadIconAsync(iconPng);
    }

    private async Task LoadIconAsync(byte[] iconPng)
    {
        Bitmap? decodedIcon = null;
        try
        {
            decodedIcon = await Task.Run(() => DecodeIcon(iconPng));
            if (decodedIcon is null)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed)
                {
                    decodedIcon.Dispose();
                    decodedIcon = null;
                    return;
                }

                Icon = decodedIcon;
                decodedIcon = null;
                OnPropertyChanged(nameof(HasIcon));
                OnPropertyChanged(nameof(ShowMonogram));
            }, DispatcherPriority.Background);
        }
        catch (Exception)
        {
            decodedIcon?.Dispose();
        }
    }

    private static string ResolveStatusTagLabel(AppSnapshot snapshot)
    {
        if (snapshot.Profile == ProfileKind.Work && snapshot.IsHidden)
        {
            return "Isolated";
        }

        if (!snapshot.IsInstalled)
        {
            return "NotInstalled";
        }

        if (snapshot.IsSystem)
        {
            return "System";
        }

        return string.Empty;
    }

    private static Bitmap? DecodeIcon(byte[]? iconPng)
    {
        if (iconPng is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(iconPng);
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
