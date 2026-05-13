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
    private CancellationTokenSource? _iconLoadCancellation;

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
        _iconLoadCancellation?.Cancel();
        _iconLoadCancellation = null;
        _icon?.Dispose();
        _icon = null;
    }

    public void RequestIconLoad()
    {
        if (_disposed || _iconLoadRequested)
        {
            return;
        }

        _iconLoadRequested = true;
        _iconLoadCancellation = new CancellationTokenSource();
        _ = LoadIconAsync(_iconLoadCancellation);
    }

    private async Task LoadIconAsync(CancellationTokenSource iconLoadCancellation)
    {
        Bitmap? decodedIcon = null;
        try
        {
            var iconPng = Snapshot.IconPng is { Length: > 0 } existingIcon
                ? existingIcon
                : await _owner
                    .LoadAppIconPngAsync(Snapshot, iconLoadCancellation.Token)
                    .ConfigureAwait(false);
            if (iconPng is not { Length: > 0 })
            {
                return;
            }

            decodedIcon = await Task.Run(
                    () => DecodeIcon(iconPng),
                    iconLoadCancellation.Token)
                .ConfigureAwait(false);
            if (decodedIcon is null)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed || iconLoadCancellation.IsCancellationRequested)
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
        catch (OperationCanceledException) when (iconLoadCancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            decodedIcon?.Dispose();
        }
        finally
        {
            iconLoadCancellation.Dispose();
            if (ReferenceEquals(_iconLoadCancellation, iconLoadCancellation))
            {
                _iconLoadCancellation = null;
            }
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
