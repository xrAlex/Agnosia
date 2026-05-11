using System.Collections.ObjectModel;
using System.Reflection;
using Agnosia.Infrastructure;
using Agnosia.Models;
using Agnosia.Platform;
using Agnosia.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Agnosia.ViewModels;

public partial class DashboardWorkspaceViewModel : ObservableObject
{
    private const int SearchRefreshDelayMs = 150;
    private const int SettingsSaveDelayMs = 350;
    private const int OnboardingMonitorDelayMs = 1500;
    private readonly IDashboardPlatformService _dashboardService;
    private readonly IPlatformEventLogReader _platformEventLogReader;
    private readonly IPermissionPlatformService _permissionService;
    private readonly IOnboardingPlatformService _onboardingService;
    private readonly IAppCommandService _appCommandService;
    private readonly IAppEventLogService _eventLogService;
    private readonly DebouncedAsyncAction _searchRefreshDebouncer;
    private readonly DashboardSettingsSaveCoordinator _settingsSaveCoordinator;
    private readonly ObservableCollection<PermissionItemViewModel> _permissionItems = [];
    private AppItemViewModel[] _visibleApps = [];
    private AppItemViewModel[] _personalApps = [];
    private AppItemViewModel[] _workApps = [];
    private bool _initialized;
    private bool _isApplyingSnapshot;
    private bool _isOperationInProgress;
    private CancellationTokenSource? _onboardingMonitorCancellation;

    public IReadOnlyList<AppItemViewModel> VisibleApps => _visibleApps;

    public ReadOnlyObservableCollection<PermissionItemViewModel> PermissionItems { get; }

    public string AppVersion => GetAppVersion();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnsupportedVisible))]
    [NotifyPropertyChangedFor(nameof(IsDashboardVisible))]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    [NotifyPropertyChangedFor(nameof(CanOpenAppsSection))]
    [NotifyPropertyChangedFor(nameof(CanOpenSettingsSection))]
    [NotifyPropertyChangedFor(nameof(OverviewHeadline))]
    [NotifyPropertyChangedFor(nameof(OverallStatusText))]
    [NotifyPropertyChangedFor(nameof(OverallStatusCaption))]
    private bool _hasLoadedSnapshot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewSectionSelected))]
    [NotifyPropertyChangedFor(nameof(IsAppsSectionSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsSectionSelected))]
    private DashboardSection _selectedSection = DashboardSection.Overview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    [NotifyPropertyChangedFor(nameof(CanStartProvisioning))]
    [NotifyPropertyChangedFor(nameof(OverviewHeadline))]
    [NotifyPropertyChangedFor(nameof(OverallStatusText))]
    [NotifyPropertyChangedFor(nameof(OverallStatusCaption))]
    [NotifyCanExecuteChangedFor(nameof(StartProvisioningCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnsupportedVisible))]
    [NotifyPropertyChangedFor(nameof(IsDashboardVisible))]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    [NotifyPropertyChangedFor(nameof(CanStartProvisioning))]
    [NotifyPropertyChangedFor(nameof(CanOpenAppsSection))]
    [NotifyPropertyChangedFor(nameof(CanOpenSettingsSection))]
    [NotifyPropertyChangedFor(nameof(OverviewHeadline))]
    [NotifyPropertyChangedFor(nameof(OverallStatusText))]
    [NotifyPropertyChangedFor(nameof(OverallStatusCaption))]
    [NotifyCanExecuteChangedFor(nameof(StartProvisioningCommand))]
    private bool _isSupported = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDashboardVisible))]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    [NotifyPropertyChangedFor(nameof(CanStartProvisioning))]
    [NotifyPropertyChangedFor(nameof(CanOpenAppsSection))]
    [NotifyPropertyChangedFor(nameof(CanOpenSettingsSection))]
    [NotifyPropertyChangedFor(nameof(WorkProfileStatusText))]
    [NotifyPropertyChangedFor(nameof(OverviewHeadline))]
    [NotifyPropertyChangedFor(nameof(OverallStatusText))]
    [NotifyPropertyChangedFor(nameof(OverallStatusCaption))]
    [NotifyCanExecuteChangedFor(nameof(StartProvisioningCommand))]
    private bool _hasSetup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartProvisioning))]
    [NotifyPropertyChangedFor(nameof(OverviewHeadline))]
    [NotifyCanExecuteChangedFor(nameof(StartProvisioningCommand))]
    private bool _isSettingUp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkProfileStatusText))]
    [NotifyPropertyChangedFor(nameof(OverviewHeadline))]
    [NotifyPropertyChangedFor(nameof(CanContinueOnboardingFromWorkProfile))]
    [NotifyPropertyChangedFor(nameof(OverallStatusText))]
    [NotifyPropertyChangedFor(nameof(OverallStatusCaption))]
    private bool _workProfileAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsPersonalProfileSelected))]
    [NotifyPropertyChangedFor(nameof(IsWorkProfileSelected))]
    private ProfileKind _selectedProfile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverallStatusText))]
    [NotifyPropertyChangedFor(nameof(OverallStatusCaption))]
    private bool _statusIsError;

    [ObservableProperty]
    private bool _showAllApps;

    [ObservableProperty]
    private bool _blockContactsSearching;

    [ObservableProperty]
    private bool _disableVpnBeforeWorkLaunch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVpnAfterFreezeClientPickerVisible))]
    [NotifyPropertyChangedFor(nameof(IsHappVpnAfterFreezeWarningVisible))]
    [NotifyPropertyChangedFor(nameof(IsTunguskaAutomationTokenVisible))]
    private bool _enableVpnAfterWorkFreeze;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFlClashVpnAfterFreezeSelected))]
    [NotifyPropertyChangedFor(nameof(IsClashMetaVpnAfterFreezeSelected))]
    [NotifyPropertyChangedFor(nameof(IsHappVpnAfterFreezeSelected))]
    [NotifyPropertyChangedFor(nameof(IsTunguskaVpnAfterFreezeSelected))]
    [NotifyPropertyChangedFor(nameof(IsHappVpnAfterFreezeWarningVisible))]
    [NotifyPropertyChangedFor(nameof(IsTunguskaAutomationTokenVisible))]
    private VpnAutomationClientKind _vpnAfterWorkFreezeClient = VpnAutomationClientKind.FlClash;

    [ObservableProperty]
    private string _tunguskaAutomationToken = string.Empty;

    [ObservableProperty]
    private bool _isLogWindowOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PermissionSummary))]
    private bool _isPermissionsWindowOpen;

    [ObservableProperty]
    private bool _loggingEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAgnosiaThemeSelected))]
    [NotifyPropertyChangedFor(nameof(IsDarkThemeSelected))]
    [NotifyPropertyChangedFor(nameof(IsLightThemeSelected))]
    private AppThemeKind _selectedTheme = AppThemeKind.Agnosia;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastRefreshSummary))]
    private DateTimeOffset? _lastRefreshedAt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnboardingVisible))]
    private bool _onboardingCompleted = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnboardingWelcomeStep))]
    [NotifyPropertyChangedFor(nameof(IsOnboardingWorkProfileStep))]
    [NotifyPropertyChangedFor(nameof(IsOnboardingPermissionsStep))]
    [NotifyPropertyChangedFor(nameof(IsOnboardingFinalStep))]
    [NotifyPropertyChangedFor(nameof(OnboardingStepLabel))]
    [NotifyPropertyChangedFor(nameof(CanContinueOnboardingFromWorkProfile))]
    private OnboardingStep _onboardingStep = OnboardingStep.Welcome;

    public int PersonalAppsCount => _personalApps.Length;

    public int WorkAppsCount => _workApps.Length;

    public bool IsPersonalProfileSelected => SelectedProfile == ProfileKind.Personal;

    public bool IsWorkProfileSelected => SelectedProfile == ProfileKind.Work;

    public string WorkProfileStatusText =>
        DashboardStatusTextFormatter.GetWorkProfileStatus(WorkProfileAvailable, HasSetup);

    public string OverviewHeadline =>
        DashboardStatusTextFormatter.GetOverviewHeadline(
            HasLoadedSnapshot,
            IsSupported,
            HasSetup,
            IsBusy,
            WorkProfileAvailable);

    public string OverallStatusText =>
        DashboardStatusTextFormatter.GetOverallStatusText(
            StatusIsError,
            HasLoadedSnapshot,
            IsBusy,
            IsSupported,
            HasSetup,
            WorkProfileAvailable);

    public string OverallStatusCaption =>
        DashboardStatusTextFormatter.GetOverallStatusCaption(
            StatusIsError,
            HasLoadedSnapshot,
            IsBusy,
            IsSupported,
            HasSetup,
            WorkProfileAvailable);

    public string LogSummary => _eventLogService.Summary;

    public string PermissionSummary =>
        _permissionItems.Count == 0
            ? "NotChecked"
            : $"GrantedCount|{_permissionItems.Count(item => item.IsGranted)}|{_permissionItems.Count}";

    public bool AreOnboardingPermissionsGranted =>
        _permissionItems.Count > 0 && _permissionItems.All(item => item.IsGranted);

    public bool IsOnboardingVisible => !OnboardingCompleted;

    public bool IsOnboardingWelcomeStep => OnboardingStep == OnboardingStep.Welcome;

    public bool IsOnboardingWorkProfileStep => OnboardingStep == OnboardingStep.WorkProfile;

    public bool IsOnboardingPermissionsStep => OnboardingStep == OnboardingStep.Permissions;

    public bool IsOnboardingFinalStep => OnboardingStep == OnboardingStep.Final;

    public bool CanContinueOnboardingFromWorkProfile => WorkProfileAvailable;

    public string OnboardingStepLabel => OnboardingStep switch
    {
        OnboardingStep.Welcome => "1",
        OnboardingStep.WorkProfile => "2",
        OnboardingStep.Permissions => "3",
        _ => "4"
    };


    public string LastRefreshSummary =>
        LastRefreshedAt is null
            ? "Never"
            : $"At|{LastRefreshedAt:HH:mm:ss}";

    public int TotalManagedAppsCount => PersonalAppsCount + WorkAppsCount;

    public int HiddenWorkAppsCount => _workApps.Count(app => app.IsHidden);

    public int InteractionAccessAppsCount => _workApps.Count(app => app.InteractionAllowed);

    public string LogOutput => _eventLogService.Output;

    public IReadOnlyList<string> LogLines => _eventLogService.Lines;

    public bool IsUnsupportedVisible => HasLoadedSnapshot && !IsSupported;

    public bool IsDashboardVisible => HasLoadedSnapshot && IsSupported && HasSetup;

    public bool IsOverviewSectionSelected => SelectedSection == DashboardSection.Overview;

    public bool IsAppsSectionSelected => SelectedSection == DashboardSection.Apps;

    public bool IsSettingsSectionSelected => SelectedSection == DashboardSection.Settings;

    public bool CanOpenAppsSection => IsDashboardVisible;

    public bool CanOpenSettingsSection => IsDashboardVisible;

    public bool IsEmptyStateVisible => IsDashboardVisible && _visibleApps.Length == 0;

    public bool IsAgnosiaThemeSelected => SelectedTheme == AppThemeKind.Agnosia;

    public bool IsDarkThemeSelected => SelectedTheme == AppThemeKind.Dark;

    public bool IsLightThemeSelected => SelectedTheme == AppThemeKind.Light;

    public bool IsVpnAfterFreezeClientPickerVisible => EnableVpnAfterWorkFreeze;

    public bool IsFlClashVpnAfterFreezeSelected => VpnAfterWorkFreezeClient == VpnAutomationClientKind.FlClash;

    public bool IsClashMetaVpnAfterFreezeSelected => VpnAfterWorkFreezeClient == VpnAutomationClientKind.ClashMeta;

    public bool IsHappVpnAfterFreezeSelected => VpnAfterWorkFreezeClient == VpnAutomationClientKind.Happ;

    public bool IsTunguskaVpnAfterFreezeSelected => VpnAfterWorkFreezeClient == VpnAutomationClientKind.Tunguska;

    public bool IsHappVpnAfterFreezeWarningVisible =>
        EnableVpnAfterWorkFreeze && VpnAfterWorkFreezeClient == VpnAutomationClientKind.Happ;

    public bool IsTunguskaAutomationTokenVisible =>
        EnableVpnAfterWorkFreeze && VpnAfterWorkFreezeClient == VpnAutomationClientKind.Tunguska;

    public bool CanStartProvisioning => !IsBusy && !_isOperationInProgress && IsSupported && !HasSetup && !IsSettingUp;

    public bool IsOperationActive => IsBusy || _isOperationInProgress;

    partial void OnSelectedSectionChanged(DashboardSection value)
    {
        if (value == DashboardSection.Apps)
        {
            _settingsSaveCoordinator.TryStartPendingCatalogRefresh();
        }
    }

    partial void OnSelectedProfileChanged(ProfileKind value)
    {
        CancelPendingSearchRefresh(); 
        RefreshVisibleApps();
    }

    partial void OnSearchTextChanged(string value)
    {
        QueueSearchRefresh();
    }
    
    partial void OnSelectedThemeChanged(AppThemeKind value)
    {
        if (!_isApplyingSnapshot)
        {
            AppThemeManager.Apply(value);
        }

        QueueSettingsSave();
    }

    partial void OnLoggingEnabledChanged(bool value)
    {
        if (value)
        {
            QueueSettingsSave();
            return;
        }

        IsLogWindowOpen = false;
        ClearLogs();
        QueueSettingsSave();
    }

    partial void OnShowAllAppsChanged(bool value) => QueueSettingsSave();

    partial void OnBlockContactsSearchingChanged(bool value) => QueueSettingsSave();

    partial void OnDisableVpnBeforeWorkLaunchChanged(bool value) => QueueSettingsSave();

    partial void OnEnableVpnAfterWorkFreezeChanged(bool value) => QueueSettingsSave();

    partial void OnVpnAfterWorkFreezeClientChanged(VpnAutomationClientKind value) => QueueSettingsSave();

    partial void OnTunguskaAutomationTokenChanged(string value) => QueueSettingsSave();

    partial void OnOnboardingCompletedChanged(bool value)
    {
        if (value)
        {
            StopOnboardingMonitor();
        }
    }

    partial void OnWorkProfileAvailableChanged(bool value)
    {
        if (value && IsOnboardingWorkProfileStep)
        {
            StartOnboardingMonitorIfNeeded();
        }
    }

    public DashboardWorkspaceViewModel() : this(UnsupportedPlatformBridge.Instance) { }

    public DashboardWorkspaceViewModel(IPlatformBridge platformBridge)
        : this(
            platformBridge,
            platformBridge,
            platformBridge,
            platformBridge,
            platformBridge,
            platformBridge,
            new BoundedAppEventLogService())
    {
    }

    public DashboardWorkspaceViewModel(
        IDashboardPlatformService dashboardService,
        IPlatformEventLogReader platformEventLogReader,
        IPermissionPlatformService permissionService,
        IOnboardingPlatformService onboardingService,
        IAppCommandService appCommandService,
        ISettingsPlatformService settingsService,
        IAppEventLogService eventLogService)
    {
        _dashboardService = dashboardService ?? throw new ArgumentNullException(nameof(dashboardService));
        _platformEventLogReader = platformEventLogReader ?? throw new ArgumentNullException(nameof(platformEventLogReader));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _onboardingService = onboardingService ?? throw new ArgumentNullException(nameof(onboardingService));
        _appCommandService = appCommandService ?? throw new ArgumentNullException(nameof(appCommandService));
        _eventLogService = eventLogService ?? throw new ArgumentNullException(nameof(eventLogService));
        _searchRefreshDebouncer = new DebouncedAsyncAction(
            TimeSpan.FromMilliseconds(SearchRefreshDelayMs),
            exception => ReportErrorOnUiThreadAsync(exception, "FilterUpdate"));
        _settingsSaveCoordinator = new DashboardSettingsSaveCoordinator(
            settingsService ?? throw new ArgumentNullException(nameof(settingsService)),
            TimeSpan.FromMilliseconds(SettingsSaveDelayMs),
            () => !_isApplyingSnapshot && HasLoadedSnapshot,
            () => !IsBusy && !_isOperationInProgress && HasLoadedSnapshot,
            () => SelectedSection == DashboardSection.Apps,
            CaptureSettingsSnapshot,
            RefreshAsync,
            SetSettingsSaveStatus,
            ResolveExceptionMessage,
            ReportErrorOnUiThreadAsync);
        PermissionItems = new ReadOnlyObservableCollection<PermissionItemViewModel>(_permissionItems);
        SelectedProfile = ProfileKind.Personal;
    }

    public async Task EnsureInitializedAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        OnboardingCompleted = await _onboardingService.LoadOnboardingCompletedAsync();
        await RefreshAsync();
        if (!OnboardingCompleted)
        {
            await AdvanceOnboardingAsync(CancellationToken.None);
            StartOnboardingMonitorIfNeeded();
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy || _isOperationInProgress)
            return;
        
        IsBusy = true;
        StatusIsError = false;
        StatusMessage = "Updating";

        try
        {
            ApplySnapshot(await _dashboardService.LoadDashboardAsync());
            HasLoadedSnapshot = true;
            StatusMessage = IsSupported
                ? "Updated"
                : "NotSupported";
        }
        catch (Exception ex)
        {
            HasLoadedSnapshot = true;
            StatusIsError = true;
            StatusMessage = ResolveExceptionMessage(ex, "UpdateState");
        }
        finally
        {
            try
            {
                await ReloadPlatformLogsAsync();
            }
            finally
            {
                IsBusy = false;
                _settingsSaveCoordinator.TryStartQueued();
            }
        }
    }

    [RelayCommand]
    private void SelectPersonal() => SelectedProfile = ProfileKind.Personal;

    [RelayCommand]
    private void SelectWork()
    {
        if (WorkProfileAvailable)
        {
            SelectedProfile = ProfileKind.Work;
        }
    }

    [RelayCommand]
    private void OpenOverviewSection() => SelectedSection = DashboardSection.Overview;

    [RelayCommand]
    private void OpenAppsSection()
    {
        if (CanOpenAppsSection)
        {
            SelectedSection = DashboardSection.Apps;
            _settingsSaveCoordinator.TryStartPendingCatalogRefresh();
        }
    }

    [RelayCommand]
    private void OpenSettingsSection()
    {
        if (CanOpenSettingsSection)
        {
            SelectedSection = DashboardSection.Settings;
        }
    }

    [RelayCommand]
    private async Task OpenLogsAsync()
    {
        if (!LoggingEnabled)
            return;
        
        await ReloadPlatformLogsAsync();
        IsLogWindowOpen = true;
    }

    [RelayCommand]
    private void CloseLogs() => IsLogWindowOpen = false;

    [RelayCommand]
    private void ClearLogs()
    {
        _eventLogService.Clear();
        NotifyLogStateChanged();
    }

    [RelayCommand]
    private async Task OpenPermissionsAsync()
    {
        await ReloadPermissionsAsync();
        IsPermissionsWindowOpen = true;
    }

    [RelayCommand]
    private void ClosePermissions() => IsPermissionsWindowOpen = false;

    [RelayCommand]
    private void SelectAgnosiaTheme() => SelectedTheme = AppThemeKind.Agnosia;

    [RelayCommand]
    private void SelectDarkTheme() => SelectedTheme = AppThemeKind.Dark;

    [RelayCommand]
    private void SelectLightTheme() => SelectedTheme = AppThemeKind.Light;

    [RelayCommand]
    private void SelectFlClashVpnAfterFreeze() => VpnAfterWorkFreezeClient = VpnAutomationClientKind.FlClash;

    [RelayCommand]
    private void SelectClashMetaVpnAfterFreeze() => VpnAfterWorkFreezeClient = VpnAutomationClientKind.ClashMeta;

    [RelayCommand]
    private void SelectHappVpnAfterFreeze() => VpnAfterWorkFreezeClient = VpnAutomationClientKind.Happ;

    [RelayCommand]
    private void SelectTunguskaVpnAfterFreeze() => VpnAfterWorkFreezeClient = VpnAutomationClientKind.Tunguska;

    [RelayCommand]
    private void StartOnboarding()
    {
        OnboardingStep = OnboardingStep.WorkProfile;
    }

    [RelayCommand]
    private async Task CheckOnboardingWorkProfileAsync()
    {
        await RefreshAsync();
        OnPropertyChanged(nameof(CanContinueOnboardingFromWorkProfile));
    }

    [RelayCommand]
    private async Task ContinueOnboardingToPermissionsAsync()
    {
        if (!WorkProfileAvailable)
        {
            StatusIsError = true;
            StatusMessage = "CompleteProvisioning";
            return;
        }

        await ReloadPermissionsAsync();
        OnboardingStep = OnboardingStep.Permissions;
        await CompleteOnboardingIfReadyAsync();
    }

    [RelayCommand]
    private async Task FinishOnboardingAsync()
    {
        await ReloadPermissionsAsync();
        if (!AreOnboardingPermissionsGranted)
        {
            StatusIsError = true;
            StatusMessage = "PermissionRequestFailed";
            StartOnboardingMonitorIfNeeded();
            return;
        }

        await CompleteOnboardingAsync();
    }

    [RelayCommand(CanExecute = nameof(CanStartProvisioning))]
    private async Task StartProvisioningAsync()
    {
        await RunOperationAsync(
            () => _onboardingService.StartProvisioningAsync(),
            "ProvisioningStarted",
            useBusyIndicator: true);
        StartOnboardingMonitorIfNeeded();
    }

    private async Task CompleteOnboardingAsync()
    {
        var result = await _onboardingService.CompleteOnboardingAsync();
        StatusIsError = !result.Succeeded;
        StatusMessage = string.IsNullOrWhiteSpace(result.Message)
            ? "ProvisioningFinished"
            : result.Message;
        if (result.Succeeded)
        {
            OnboardingCompleted = true;
            await RefreshAsync();
        }
    }

    internal Task CloneAsync(AppItemViewModel app) =>
        RunAppOperationAsync(
            app,
            snapshot => _appCommandService.CloneAsync(snapshot),
            app.Profile == ProfileKind.Personal
                ? "CopyPersonalToWorkFinished"
                : "CopyWorkToPersonalFinished");

    internal async Task MoveToWorkAsync(AppItemViewModel app)
    {
        if (!app.CanMoveToWork || !TryBeginOperation())
        {
            return;
        }

        var shouldRefresh = false;

        try
        {
            var snapshot = app.Snapshot;
            var cloneResult = await _appCommandService.CloneAsync(snapshot);
            if (!cloneResult.Succeeded)
            {
                StatusIsError = true;
                StatusMessage = ResolveOperationMessage(cloneResult.Message, "CloneFailed");
                return;
            }

            shouldRefresh = true;
            var uninstallResult = await _appCommandService.UninstallAsync(snapshot);
            StatusIsError = !uninstallResult.Succeeded;
            StatusMessage = uninstallResult.Succeeded
                ? "MovedToWork"
                : $"MovedToWorkDeleteFailed|{ResolveOperationMessage(uninstallResult.Message, "DeleteFailed")}";
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = ResolveExceptionMessage(ex, "MoveToWorkFailed");
        }
        finally
        {
            if (shouldRefresh)
            {
                try
                {
                    ApplySnapshot(await _dashboardService.LoadDashboardAsync());
                }
                catch (Exception ex) when (!StatusIsError)
                {
                    StatusIsError = true;
                    StatusMessage = ResolveExceptionMessage(ex, "RefreshAfterOpFailed");
                }
                catch (Exception)
                {
                    StatusMessage = $"{StatusMessage}|ManualRefreshHint";
                }
            }

            try
            {
                await ReloadPlatformLogsAsync();
            }
            finally
            {
                EndOperation();
                _settingsSaveCoordinator.TryStartQueued();
            }
        }
    }

    internal Task UninstallAsync(AppItemViewModel app) =>
        RunAppOperationAsync(app, snapshot => _appCommandService.UninstallAsync(snapshot), "Deleted");

    internal Task ToggleFrozenAsync(AppItemViewModel app) =>
        RunAppOperationAsync(
            app,
            snapshot => _appCommandService.SetFrozenAsync(snapshot, !snapshot.IsHidden),
            app.IsHidden ? "Restored" : "Hidden");

    internal Task ForceFreezeAsync(AppItemViewModel app) =>
        RunAppOperationAsync(app, snapshot => _appCommandService.ForceFreezeAsync(snapshot), "ForceHidden");

    internal Task CreateShortcutAsync(AppItemViewModel app) =>
        RunAppOperationAsync(app, snapshot => _appCommandService.CreateShortcutAsync(snapshot), "ShortcutRequested");

    internal Task LaunchAsync(AppItemViewModel app) =>
        RunAppOperationAsync(app, snapshot => _appCommandService.LaunchAsync(snapshot), "Launching");

    internal Task ToggleInteractionAccessAsync(AppItemViewModel app) =>
        RunAppOperationAsync(
            app,
            snapshot => _appCommandService.SetInteractionAccessAsync(snapshot, !snapshot.InteractionAllowed),
            app.InteractionAllowed ? "InteractionDisabled" : "InteractionEnabled");

    internal async Task RequestPermissionAsync(PermissionItemViewModel permission)
    {
        if (!permission.CanRequest || !TryBeginOperation())
        {
            return;
        }

        try
        {
            var result = await _permissionService.RequestPermissionAsync(permission.Kind);
            StatusIsError = !result.Succeeded;
            StatusMessage = string.IsNullOrWhiteSpace(result.Message)
                ? "PermissionRequestOpened"
                : result.Message;
            await ReloadPermissionsAsync();
            await CompleteOnboardingIfReadyAsync();
            StartOnboardingMonitorIfNeeded();
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = ResolveExceptionMessage(ex, "PermissionRequestFailed");
        }
        finally
        {
            try
            {
                await ReloadPlatformLogsAsync();
            }
            finally
            {
                EndOperation();
                _settingsSaveCoordinator.TryStartQueued();
            }
        }
    }

    private void ApplySnapshot(DashboardSnapshot snapshot)
    {
        _isApplyingSnapshot = true;
        try
        {
            IsSupported = snapshot.IsSupported;
            HasSetup = snapshot.HasSetup;
            IsSettingUp = snapshot.IsSettingUp;
            WorkProfileAvailable = snapshot.WorkProfileAvailable;
            if (IsSupported && !HasSetup && OnboardingCompleted)
            {
                OnboardingCompleted = false;
                OnboardingStep = OnboardingStep.Welcome;
            }

            OnPropertyChanged(nameof(CanContinueOnboardingFromWorkProfile));

            ShowAllApps = snapshot.Settings.ShowAllApps;
            BlockContactsSearching = snapshot.Settings.BlockContactsSearching;
            DisableVpnBeforeWorkLaunch = snapshot.Settings.DisableVpnBeforeWorkLaunch;
            EnableVpnAfterWorkFreeze = snapshot.Settings.EnableVpnAfterWorkFreeze;
            VpnAfterWorkFreezeClient = snapshot.Settings.VpnAfterWorkFreezeClient;
            TunguskaAutomationToken = snapshot.Settings.TunguskaAutomationToken;
            LoggingEnabled = snapshot.Settings.LoggingEnabled;
            SelectedTheme = snapshot.Settings.Theme;
            _settingsSaveCoordinator.SetLoadedShowAllApps(snapshot.Settings.ShowAllApps);

            var previousPersonalApps = _personalApps;
            var previousWorkApps = _workApps;
            _personalApps = snapshot.PersonalApps.Select(app => new AppItemViewModel(this, app)).ToArray();
            _workApps = snapshot.WorkApps.Select(app => new AppItemViewModel(this, app)).ToArray();
            LastRefreshedAt = DateTimeOffset.Now;

            if (!WorkProfileAvailable && SelectedProfile == ProfileKind.Work)
            {
                SelectedProfile = ProfileKind.Personal;
            }

            OnPropertyChanged(nameof(PersonalAppsCount));
            OnPropertyChanged(nameof(WorkAppsCount));
            NotifyOverviewMetricsChanged();

            EnsureSelectedSectionIsAvailable();
            RefreshVisibleApps();
            DisposeAppItems(previousPersonalApps);
            DisposeAppItems(previousWorkApps);
        }
        finally
        {
            _isApplyingSnapshot = false;
        }
    }

    private void RefreshVisibleApps()
    {
        SetVisibleApps(AppCatalogFilter.FilterVisibleApps(_personalApps, _workApps, SelectedProfile, SearchText));
    }

    private void QueueSearchRefresh()
    {
        _searchRefreshDebouncer.Schedule(RefreshVisibleAppsAfterDelayAsync);
    }

    private async Task RefreshVisibleAppsAfterDelayAsync(CancellationToken cancellationToken)
    {
        var personalApps = _personalApps;
        var workApps = _workApps;
        var selectedProfile = SelectedProfile;
        var searchText = SearchText;
        var visibleApps = await Task.Run(
            () => AppCatalogFilter.FilterVisibleApps(personalApps, workApps, selectedProfile, searchText),
            cancellationToken);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!cancellationToken.IsCancellationRequested
                && ReferenceEquals(personalApps, _personalApps)
                && ReferenceEquals(workApps, _workApps)
                && selectedProfile == SelectedProfile
                && string.Equals(searchText, SearchText, StringComparison.Ordinal))
            {
                SetVisibleApps(visibleApps);
            }
        }, DispatcherPriority.Background);
    }

    private void SetVisibleApps(AppItemViewModel[] visibleApps)
    {
        _visibleApps = visibleApps;
        OnPropertyChanged(nameof(VisibleApps));
        OnPropertyChanged(nameof(IsEmptyStateVisible));
    }

    private void CancelPendingSearchRefresh()
    {
        _searchRefreshDebouncer.Cancel();
    }

    private Task RunAppOperationAsync(
        AppItemViewModel app,
        Func<AppSnapshot, Task<OperationResult>> operation,
        string successFallback) =>
        RunOperationAsync(() => operation(app.Snapshot), successFallback, useBusyIndicator: false);

    private void EnsureSelectedSectionIsAvailable()
    {
        if (!IsDashboardVisible && SelectedSection != DashboardSection.Overview)
        {
            SelectedSection = DashboardSection.Overview;
        }
    }

    private async Task RunOperationAsync(
        Func<Task<OperationResult>> operation,
        string successFallback,
        bool useBusyIndicator)
    {
        if (!TryBeginOperation())
        {
            return;
        }

        if (useBusyIndicator)
        {
            IsBusy = true;
        }

        try
        {
            var result = await operation();
            StatusIsError = !result.Succeeded;
            StatusMessage = string.IsNullOrWhiteSpace(result.Message) ? successFallback : result.Message;

            if (result.Succeeded)
            {
                ApplySnapshot(await _dashboardService.LoadDashboardAsync());
            }
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = ResolveExceptionMessage(ex, "GenericOperationFailed");
        }
        finally
        {
            try
            {
                await ReloadPlatformLogsAsync();
            }
            finally
            {
                if (useBusyIndicator)
                {
                    IsBusy = false;
                }

                EndOperation();
                _settingsSaveCoordinator.TryStartQueued();
            }
        }
    }

    private void StartOnboardingMonitorIfNeeded()
    {
        if (OnboardingCompleted
            || OnboardingStep == OnboardingStep.Welcome
            || OnboardingStep == OnboardingStep.Final
            || _onboardingMonitorCancellation is not null)
        {
            return;
        }

        if (OnboardingStep == OnboardingStep.WorkProfile && !IsSettingUp && !WorkProfileAvailable)
        {
            return;
        }

        _onboardingMonitorCancellation = new CancellationTokenSource();
        _ = MonitorOnboardingAsync(_onboardingMonitorCancellation.Token);
    }

    private void StopOnboardingMonitor()
    {
        _onboardingMonitorCancellation?.Cancel();
    }

    private async Task MonitorOnboardingAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !OnboardingCompleted)
            {
                await AdvanceOnboardingAsync(cancellationToken);

                if (OnboardingCompleted
                    || OnboardingStep == OnboardingStep.Welcome
                    || OnboardingStep == OnboardingStep.Final)
                {
                    return;
                }

                await Task.Delay(OnboardingMonitorDelayMs, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            await ReportErrorOnUiThreadAsync(ex, "UpdateState");
        }
        finally
        {
            if (_onboardingMonitorCancellation?.Token == cancellationToken)
            {
                _onboardingMonitorCancellation.Dispose();
                _onboardingMonitorCancellation = null;
            }
        }
    }

    private async Task AdvanceOnboardingAsync(CancellationToken cancellationToken)
    {
        if (IsBusy || _isOperationInProgress)
        {
            return;
        }

        if (OnboardingStep == OnboardingStep.WorkProfile)
        {
            await RefreshAsync();
            if (!WorkProfileAvailable)
            {
                return;
            }

            await ReloadPermissionsAsync();
            OnboardingStep = OnboardingStep.Permissions;
        }

        if (OnboardingStep == OnboardingStep.Permissions)
        {
            await ReloadPermissionsAsync();
            await CompleteOnboardingIfReadyAsync();
        }
    }

    private async Task CompleteOnboardingIfReadyAsync()
    {
        OnPropertyChanged(nameof(AreOnboardingPermissionsGranted));
        if (OnboardingStep == OnboardingStep.Permissions && AreOnboardingPermissionsGranted)
        {
            OnboardingStep = OnboardingStep.Final;
        }
    }

    private bool TryBeginOperation()
    {
        if (IsBusy || _isOperationInProgress)
        {
            return false;
        }

        _isOperationInProgress = true;
        OnPropertyChanged(nameof(CanStartProvisioning));
        OnPropertyChanged(nameof(IsOperationActive));
        StartProvisioningCommand.NotifyCanExecuteChanged();
        return true;
    }

    private void EndOperation()
    {
        if (!_isOperationInProgress)
        {
            return;
        }

        _isOperationInProgress = false;
        OnPropertyChanged(nameof(CanStartProvisioning));
        OnPropertyChanged(nameof(IsOperationActive));
        StartProvisioningCommand.NotifyCanExecuteChanged();
    }

    private AppSettingsSnapshot CaptureSettingsSnapshot() =>
        new(
            ShowAllApps,
            BlockContactsSearching,
            DisableVpnBeforeWorkLaunch,
            LoggingEnabled,
            SelectedTheme,
            EnableVpnAfterWorkFreeze,
            VpnAfterWorkFreezeClient,
            TunguskaAutomationToken);

    private void QueueSettingsSave()
    {
        _settingsSaveCoordinator.Queue();
    }

    private void SetSettingsSaveStatus(bool isError, string? message)
    {
        StatusIsError = isError;
        if (!string.IsNullOrWhiteSpace(message))
        {
            StatusMessage = message;
        }
    }

    private void NotifyLogStateChanged()
    {
        OnPropertyChanged(nameof(LogSummary));
        OnPropertyChanged(nameof(LogOutput));
        OnPropertyChanged(nameof(LogLines));
    }

    private async Task ReloadPlatformLogsAsync()
    {
        if (!LoggingEnabled)
        {
            return;
        }

        var logs = await _platformEventLogReader.LoadRecentLogsAsync();
        ImportPlatformLogs(logs);
    }

    private async Task ReloadPermissionsAsync()
    {
        var snapshots = await _permissionService.LoadPermissionsAsync();
        _permissionItems.Clear();
        foreach (var snapshot in snapshots)
        {
            _permissionItems.Add(new PermissionItemViewModel(this, snapshot));
        }

        OnPropertyChanged(nameof(PermissionSummary));
        OnPropertyChanged(nameof(AreOnboardingPermissionsGranted));
    }

    private void ImportPlatformLogs(IEnumerable<AppLogEntry> logs)
    {
        if (_eventLogService.ImportPlatformLogs(logs))
        {
            NotifyLogStateChanged();
        }
    }

    private async Task ReportErrorOnUiThreadAsync(Exception exception, string fallbackMessage)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            StatusIsError = true;
            StatusMessage = ResolveExceptionMessage(exception, fallbackMessage);
        }, DispatcherPriority.Background);
    }

    private static string ResolveOperationMessage(string? message, string fallback) =>
        string.IsNullOrWhiteSpace(message) ? fallback : message;

    private static string ResolveExceptionMessage(Exception _, string fallback) => fallback;

    private static string GetAppVersion() =>
        typeof(DashboardWorkspaceViewModel)
            .Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            .Split('+')[0] ?? "0.9";

    private static void DisposeAppItems(IEnumerable<AppItemViewModel> apps)
    {
        foreach (var app in apps)
        {
            app.Dispose();
        }
    }

    private void NotifyOverviewMetricsChanged()
    {
        OnPropertyChanged(nameof(OverviewHeadline));
        OnPropertyChanged(nameof(TotalManagedAppsCount));
        OnPropertyChanged(nameof(HiddenWorkAppsCount));
        OnPropertyChanged(nameof(InteractionAccessAppsCount));
    }
}
