using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading.Channels;
using Agnosia.Infrastructure;
using Agnosia.Models;
using Agnosia.Platform;
using Agnosia.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stopwatch = System.Diagnostics.Stopwatch;
using Trace = System.Diagnostics.Trace;

namespace Agnosia.ViewModels;

public partial class DashboardWorkspaceViewModel : ObservableObject
{
    private const int SearchRefreshDelayMs = 150;
    private const int SettingsSaveDelayMs = 350;
    private const int OnboardingMonitorDelayMs = 1500;
    private const int IconBatchDelayMs = 60;
    private static readonly TimeSpan ResumeRefreshMinimumInterval = TimeSpan.FromSeconds(2);
    private const string StaleApkMessageMarker = "APK изменился";
    private static readonly PermissionKind[] RequiredOnboardingPermissionKinds =
    [
        PermissionKind.WorkProfile,
        PermissionKind.UsageStats,
        PermissionKind.Notifications,
        PermissionKind.VpnControl,
        PermissionKind.PackageInstall,
        PermissionKind.Overlay
    ];

    private readonly IDashboardPlatformService _dashboardService;
    private readonly IPlatformEventLogReader _platformEventLogReader;
    private readonly IPermissionPlatformService _permissionService;
    private readonly IOnboardingPlatformService _onboardingService;
    private readonly IAppCommandService _appCommandService;
    private readonly ISettingsPlatformService _settingsService;
    private readonly IAppEventLogService _eventLogService;
    private readonly Func<Action, DispatcherPriority, ValueTask> _invokeOnUiThreadAsync;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly DebouncedAsyncAction _searchRefreshDebouncer;
    private readonly DashboardSettingsSaveCoordinator _settingsSaveCoordinator;
    private readonly SemaphoreSlim _iconLoadGate = new(1, 1);
    private readonly Lock _iconBatchProcessorSync = new();
    private readonly Lock _permissionReloadSync = new();
    private readonly Channel<PendingIconLoad> _pendingIconLoads = Channel.CreateUnbounded<PendingIconLoad>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    private readonly Dictionary<AppItemKey, AppItemViewModel> _appItemCache = [];
    private readonly ObservableCollection<PermissionItemViewModel> _permissionItems = [];
    private AppItemViewModel[] _visibleApps = [];
    private AppItemViewModel[] _personalApps = [];
    private AppItemViewModel[] _workApps = [];
    private DashboardSnapshot? _lastProfileSnapshot;
    private Task? _iconBatchProcessor;
    private Task? _permissionReloadTask;
    private bool _initialized;
    private bool _isApplyingSnapshot;
    private bool _isOperationInProgress;
    private bool _isPreparingOnboardingPermissions;
    private bool _refreshPermissionsOnResume;
    private bool _inventoryLoadInProgress;
    private int _inventoryLoadGeneration;
    private CancellationTokenSource? _inventoryLoadCancellation;
    private CancellationTokenSource? _onboardingMonitorCancellation;
    private DateTimeOffset? _lastResumeRefreshAt;

    public IReadOnlyList<AppItemViewModel> VisibleApps => _visibleApps;

    public ReadOnlyObservableCollection<PermissionItemViewModel> PermissionItems { get; }

    public IReadOnlyList<VpnAutomationClientOptionViewModel> VpnAfterFreezeClientOptions { get; }

    public string AppVersion => GetAppVersion();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnsupportedVisible))]
    [NotifyPropertyChangedFor(nameof(IsDashboardVisible))]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsAppInventoryProgressVisible))]
    [NotifyPropertyChangedFor(nameof(CanOpenAppsSection))]
    [NotifyPropertyChangedFor(nameof(CanOpenSettingsSection))]
    [NotifyPropertyChangedFor(nameof(OverviewHeadline))]
    [NotifyPropertyChangedFor(nameof(OverallStatusText))]
    [NotifyPropertyChangedFor(nameof(OverallStatusCaption))]
    private partial bool HasLoadedSnapshot { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewSectionSelected))]
    [NotifyPropertyChangedFor(nameof(IsAppsSectionSelected))]
    [NotifyPropertyChangedFor(nameof(IsSettingsSectionSelected))]
    private partial DashboardSection SelectedSection { get; set; } = DashboardSection.Overview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartProvisioning))]
    [NotifyPropertyChangedFor(nameof(OverviewHeadline))]
    [NotifyPropertyChangedFor(nameof(OverallStatusText))]
    [NotifyPropertyChangedFor(nameof(OverallStatusCaption))]
    [NotifyPropertyChangedFor(nameof(IsOperationActive))]
    [NotifyCanExecuteChangedFor(nameof(StartProvisioningCommand))]
    private partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnsupportedVisible))]
    [NotifyPropertyChangedFor(nameof(IsDashboardVisible))]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsAppInventoryProgressVisible))]
    [NotifyPropertyChangedFor(nameof(CanStartProvisioning))]
    [NotifyPropertyChangedFor(nameof(CanOpenAppsSection))]
    [NotifyPropertyChangedFor(nameof(CanOpenSettingsSection))]
    [NotifyPropertyChangedFor(nameof(OverviewHeadline))]
    [NotifyPropertyChangedFor(nameof(OverallStatusText))]
    [NotifyPropertyChangedFor(nameof(OverallStatusCaption))]
    [NotifyCanExecuteChangedFor(nameof(StartProvisioningCommand))]
    private partial bool IsSupported { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDashboardVisible))]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsAppInventoryProgressVisible))]
    [NotifyPropertyChangedFor(nameof(CanOpenAppsSection))]
    [NotifyPropertyChangedFor(nameof(CanOpenSettingsSection))]
    [NotifyPropertyChangedFor(nameof(WorkProfileStatusText))]
    [NotifyPropertyChangedFor(nameof(OverviewHeadline))]
    [NotifyPropertyChangedFor(nameof(OverallStatusText))]
    [NotifyPropertyChangedFor(nameof(OverallStatusCaption))]
    public partial bool HasSetup { get; set; }

    [ObservableProperty]
    private partial bool IsSettingUp { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkProfileStatusText))]
    [NotifyPropertyChangedFor(nameof(OverviewHeadline))]
    [NotifyPropertyChangedFor(nameof(CanContinueOnboardingFromWorkProfile))]
    [NotifyPropertyChangedFor(nameof(IsOnboardingWorkProfileStep))]
    [NotifyPropertyChangedFor(nameof(IsOnboardingPermissionsStep))]
    [NotifyPropertyChangedFor(nameof(OnboardingStepLabel))]
    [NotifyPropertyChangedFor(nameof(OverallStatusText))]
    [NotifyPropertyChangedFor(nameof(OverallStatusCaption))]
    public partial bool WorkProfileAvailable { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPersonalProfileSelected))]
    [NotifyPropertyChangedFor(nameof(IsWorkProfileSelected))]
    private partial ProfileKind SelectedProfile { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    private partial bool HasLoadedInventory { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsAppInventoryProgressVisible))]
    private partial bool IsInventoryLoading { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Ready";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverallStatusText))]
    [NotifyPropertyChangedFor(nameof(OverallStatusCaption))]
    public partial bool StatusIsError { get; set; }

    [ObservableProperty]
    public partial bool ShowAllApps { get; set; }

    [ObservableProperty]
    public partial bool DisableVpnBeforeWorkLaunch { get; set; }

    [ObservableProperty]
    public partial bool CrossProfileFileShuttleEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVpnAfterFreezeClientPickerVisible))]
    [NotifyPropertyChangedFor(nameof(IsToggleOnlyVpnAfterFreezeWarningVisible))]
    [NotifyPropertyChangedFor(nameof(IsTunguskaAutomationTokenVisible))]
    public partial bool EnableVpnAfterWorkFreeze { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsToggleOnlyVpnAfterFreezeWarningVisible))]
    [NotifyPropertyChangedFor(nameof(IsTunguskaAutomationTokenVisible))]
    private partial VpnAutomationClientKind VpnAfterWorkFreezeClient { get; set; } = VpnAutomationClientKind.FlClash;

    [ObservableProperty]
    public partial string TunguskaAutomationToken { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLogWindowOpen { get; set; }

    [ObservableProperty]
    public partial bool IsPermissionsWindowOpen { get; set; }

    [ObservableProperty]
    public partial bool IsAppControlWindowOpen { get; set; }

    [ObservableProperty]
    public partial AppItemViewModel? SelectedApp { get; set; }

    [ObservableProperty]
    public partial bool LoggingEnabled { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAgnosiaThemeSelected))]
    [NotifyPropertyChangedFor(nameof(IsDarkThemeSelected))]
    [NotifyPropertyChangedFor(nameof(IsLightThemeSelected))]
    private partial AppThemeKind SelectedTheme { get; set; } = AppThemeKind.Agnosia;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastRefreshSummary))]
    private partial DateTimeOffset? LastRefreshedAt { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnboardingVisible))]
    private partial bool OnboardingCompleted { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkProfileStatusText))]
    private partial WorkProfileStateKind WorkProfileState { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorkProfileRecoveryVisible))]
    [NotifyPropertyChangedFor(nameof(WorkProfileRecoveryTitle))]
    [NotifyPropertyChangedFor(nameof(WorkProfileRecoveryMessage))]
    [NotifyPropertyChangedFor(nameof(IsWorkProfileRecoveryOnboardingRestart))]
    private partial WorkProfileRecoveryKind WorkProfileRecovery { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorkProfileRecoveryVisible))]
    public partial bool WorkProfileRecoveryDismissed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnboardingWelcomeStep))]
    [NotifyPropertyChangedFor(nameof(IsOnboardingWorkProfileStep))]
    [NotifyPropertyChangedFor(nameof(IsOnboardingPermissionsStep))]
    [NotifyPropertyChangedFor(nameof(IsOnboardingFinalStep))]
    [NotifyPropertyChangedFor(nameof(OnboardingStepLabel))]
    [NotifyPropertyChangedFor(nameof(CanContinueOnboardingFromWorkProfile))]
    public partial OnboardingStep OnboardingStep { get; set; } = OnboardingStep.Welcome;

    public int PersonalAppsCount => _personalApps.Length;

    public int WorkAppsCount => _workApps.Length;

    public bool IsPersonalProfileSelected => SelectedProfile == ProfileKind.Personal;

    public bool IsWorkProfileSelected => SelectedProfile == ProfileKind.Work;

    public string WorkProfileStatusText =>
        DashboardStatusTextFormatter.GetWorkProfileStatus(
            WorkProfileState,
            WorkProfileAvailable,
            HasSetup);

    public string OverviewHeadline =>
        DashboardStatusTextFormatter.GetOverviewHeadline(
            HasLoadedSnapshot,
            IsSupported,
            HasSetup,
            IsOperationActive,
            WorkProfileAvailable);

    public string OverallStatusText =>
        DashboardStatusTextFormatter.GetOverallStatusText(
            StatusIsError,
            HasLoadedSnapshot,
            IsOperationActive,
            IsSupported,
            HasSetup,
            WorkProfileAvailable);

    public string OverallStatusCaption =>
        DashboardStatusTextFormatter.GetOverallStatusCaption(
            StatusIsError,
            HasLoadedSnapshot,
            IsOperationActive,
            IsSupported,
            HasSetup,
            WorkProfileAvailable);

    public string LogSummary => _eventLogService.Summary;

    public string PermissionSummary =>
        _permissionItems.Count == 0
            ? "NotChecked"
            : $"GrantedCount|{_permissionItems.Count(item => item.IsGranted)}|{_permissionItems.Count}";

    public bool AreOnboardingPermissionsGranted =>
        RequiredOnboardingPermissionKinds.All(kind =>
            _permissionItems.Any(item => item.Kind == kind && item.IsGranted));

    public bool IsWorkProfileRecoveryVisible =>
        WorkProfileRecovery != WorkProfileRecoveryKind.None
        && !WorkProfileRecoveryDismissed;

    public string WorkProfileRecoveryTitle => WorkProfileRecovery switch
    {
        WorkProfileRecoveryKind.UpdateFailedDeleteWorkProfile => "Обновление не удалось",
        WorkProfileRecoveryKind.ProbablyDeletedRestartOnboarding => "Рабочий профиль удалён",
        WorkProfileRecoveryKind.DeleteWorkProfile => "Удалите рабочий профиль",
        _ => "Проблема с рабочим профилем"
    };

    public string WorkProfileRecoveryMessage => WorkProfileRecovery switch
    {
        WorkProfileRecoveryKind.UpdateFailedDeleteWorkProfile =>
            "Обновление не удалось, удалите профиль.",
        WorkProfileRecoveryKind.ProbablyDeletedRestartOnboarding =>
            "Вероятно, рабочий профиль был удалён или Android завершает его удаление. Нажмите OK, чтобы начать настройку заново.",
        WorkProfileRecoveryKind.DeleteWorkProfile =>
            "Этот профиль недоступен или не управляется Agnosia. Удалите его в настройках Android, затем вернитесь в Agnosia и создайте рабочий профиль заново.",
        _ => string.Empty
    };

    public bool IsWorkProfileRecoveryOnboardingRestart =>
        WorkProfileRecovery == WorkProfileRecoveryKind.ProbablyDeletedRestartOnboarding;

    public bool IsOnboardingVisible => !OnboardingCompleted;

    public bool IsOnboardingWelcomeStep => OnboardingStep == OnboardingStep.Welcome;

    public bool IsOnboardingWorkProfileStep =>
        OnboardingStep == OnboardingStep.WorkProfile
        && !WorkProfileAvailable
        && !_isPreparingOnboardingPermissions;

    public bool IsOnboardingPermissionsStep =>
        OnboardingStep == OnboardingStep.Permissions
        || (OnboardingStep == OnboardingStep.WorkProfile
            && WorkProfileAvailable
            && _permissionItems.Count > 0);

    public bool IsOnboardingFinalStep => OnboardingStep == OnboardingStep.Final;

    public bool CanContinueOnboardingFromWorkProfile => WorkProfileAvailable;

    public string OnboardingStepLabel =>
        IsOnboardingWelcomeStep ? "1" :
        IsOnboardingWorkProfileStep ? "2" :
        IsOnboardingPermissionsStep ? "3" :
        IsOnboardingFinalStep ? "4" :
        string.Empty;


    public string LastRefreshSummary =>
        LastRefreshedAt is null
            ? "Never"
            : $"At|{LastRefreshedAt:HH:mm:ss}";

    public int TotalManagedAppsCount => PersonalAppsCount + WorkAppsCount;

    public int HiddenWorkAppsCount => _workApps.Count(app => app.IsHidden);

    public int InteractionAccessAppsCount => _workApps.Count(app => app.InteractionAllowed);

    public string LogOutput => _eventLogService.Output;

    public string LogOutputWithDeviceInfo
    {
        get
        {
            var deviceInfo = _platformEventLogReader.GetDeviceInfoString();
            var logs = _eventLogService.Output;
            return _eventLogService.Output.Length == 0
                ? deviceInfo
                : deviceInfo + Environment.NewLine + new string('=', 44) + Environment.NewLine + logs;
        }
    }

    public IReadOnlyList<string> LogLines => _eventLogService.Lines;

    public bool IsUnsupportedVisible => HasLoadedSnapshot && !IsSupported;

    public bool IsDashboardVisible => HasLoadedSnapshot && IsSupported && HasSetup;

    public bool IsOverviewSectionSelected => SelectedSection == DashboardSection.Overview;

    public bool IsAppsSectionSelected => SelectedSection == DashboardSection.Apps;

    public bool IsSettingsSectionSelected => SelectedSection == DashboardSection.Settings;

    public bool CanOpenAppsSection => IsDashboardVisible;

    public bool CanOpenSettingsSection => IsDashboardVisible;

    public bool IsEmptyStateVisible =>
        IsDashboardVisible && HasLoadedInventory && !IsInventoryLoading && _visibleApps.Length == 0;

    public bool IsAppInventoryProgressVisible => IsDashboardVisible && IsInventoryLoading;

    public bool IsAgnosiaThemeSelected => SelectedTheme == AppThemeKind.Agnosia;

    public bool IsDarkThemeSelected => SelectedTheme == AppThemeKind.Dark;

    public bool IsLightThemeSelected => SelectedTheme == AppThemeKind.Light;

    public bool IsVpnAfterFreezeClientPickerVisible => EnableVpnAfterWorkFreeze;

    public bool IsToggleOnlyVpnAfterFreezeWarningVisible =>
        EnableVpnAfterWorkFreeze
        && (VpnAfterWorkFreezeClient == VpnAutomationClientKind.Happ
            || VpnAfterWorkFreezeClient == VpnAutomationClientKind.Exclave
            || VpnAfterWorkFreezeClient == VpnAutomationClientKind.Husi
            || VpnAfterWorkFreezeClient == VpnAutomationClientKind.NekoBoxPlus);

    public bool IsTunguskaAutomationTokenVisible =>
        EnableVpnAfterWorkFreeze && VpnAfterWorkFreezeClient == VpnAutomationClientKind.Tunguska;

    public bool CanStartProvisioning =>
        !IsBusy
        && !_isOperationInProgress
        && IsSupported
        && !WorkProfileAvailable;

    public bool IsOperationActive => IsBusy || _isOperationInProgress;

    partial void OnSelectedSectionChanged(DashboardSection value)
    {
        if (value == DashboardSection.Apps)
        {
            _settingsSaveCoordinator.TryStartPendingCatalogRefresh();
            StartInventoryLoadIfNeeded();
            return;
        }

        CancelVisibleIconLoads();
    }

    partial void OnSelectedProfileChanged(ProfileKind value)
    {
        CancelVisibleIconLoads();
        CancelPendingSearchRefresh();
        RefreshVisibleApps();
    }

    partial void OnSearchTextChanged(string value) => QueueSearchRefresh();

    partial void OnSelectedThemeChanged(AppThemeKind value)
    {
        if (!_isApplyingSnapshot) AppThemeManager.Apply(value);

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

    partial void OnDisableVpnBeforeWorkLaunchChanged(bool value) => QueueSettingsSave();

    partial void OnCrossProfileFileShuttleEnabledChanged(bool value) => QueueSettingsSave();

    partial void OnEnableVpnAfterWorkFreezeChanged(bool value) => QueueSettingsSave();

    partial void OnVpnAfterWorkFreezeClientChanged(VpnAutomationClientKind value)
    {
        foreach (var option in VpnAfterFreezeClientOptions) option.NotifySelectionChanged();

        QueueSettingsSave();
    }

    partial void OnTunguskaAutomationTokenChanged(string value) => QueueSettingsSave();

    partial void OnOnboardingCompletedChanged(bool value)
    {
        if (value) StopOnboardingMonitor();
    }

    partial void OnWorkProfileAvailableChanged(bool value)
    {
        if (value && OnboardingStep == OnboardingStep.WorkProfile)
        {
            SetPreparingOnboardingPermissions(true);
            StartOnboardingMonitorIfNeeded();
        }
    }

    public DashboardWorkspaceViewModel() : this(UnsupportedPlatformBridge.Instance)
    {
    }

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
        IAppEventLogService eventLogService,
        Func<Action, DispatcherPriority, ValueTask>? invokeOnUiThreadAsync = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _dashboardService = dashboardService ?? throw new ArgumentNullException(nameof(dashboardService));
        _platformEventLogReader =
            platformEventLogReader ?? throw new ArgumentNullException(nameof(platformEventLogReader));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _onboardingService = onboardingService ?? throw new ArgumentNullException(nameof(onboardingService));
        _appCommandService = appCommandService ?? throw new ArgumentNullException(nameof(appCommandService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _eventLogService = eventLogService ?? throw new ArgumentNullException(nameof(eventLogService));
        _invokeOnUiThreadAsync = invokeOnUiThreadAsync ?? InvokeOnAvaloniaUiThreadAsync;
        _delayAsync = delayAsync ?? Task.Delay;
        _searchRefreshDebouncer = new DebouncedAsyncAction(
            TimeSpan.FromMilliseconds(SearchRefreshDelayMs),
            exception => ReportErrorOnUiThreadAsync(exception, "FilterUpdate"),
            _delayAsync);
        _settingsSaveCoordinator = new DashboardSettingsSaveCoordinator(
            _settingsService,
            TimeSpan.FromMilliseconds(SettingsSaveDelayMs),
            () => !_isApplyingSnapshot && HasLoadedSnapshot,
            () => !IsBusy && !_isOperationInProgress && HasLoadedSnapshot,
            () => SelectedSection == DashboardSection.Apps,
            CaptureSettingsSnapshot,
            RefreshAsync,
            SetSettingsSaveStatus,
            ResolveExceptionMessage,
            ReportErrorOnUiThreadAsync,
            _delayAsync);
        PermissionItems = new ReadOnlyObservableCollection<PermissionItemViewModel>(_permissionItems);
        VpnAfterFreezeClientOptions = CreateVpnAfterFreezeClientOptions();
        SelectedProfile = ProfileKind.Personal;
    }

    public async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        _initialized = true;
        OnboardingCompleted = await _onboardingService.LoadOnboardingCompletedAsync();
        await RefreshDashboardAsync(false);
        if (!OnboardingCompleted)
        {
            await AdvanceOnboardingAsync(CancellationToken.None);
            StartOnboardingMonitorIfNeeded();
        }
    }

    public void HandlePrimaryActivityResumed()
    {
        var handledPermissionResume = false;
        if (_refreshPermissionsOnResume)
        {
            handledPermissionResume = true;
            _refreshPermissionsOnResume = false;
            _ = RefreshPermissionsAfterResumeAsync();
        }

        if (handledPermissionResume || StatusIsError) return;

        _ = RefreshDashboardAfterResumeAsync();
    }

    private async Task RefreshDashboardAfterResumeAsync()
    {
        if (!_initialized || IsBusy || _isOperationInProgress) return;

        var now = DateTimeOffset.UtcNow;
        if (_lastResumeRefreshAt is not null
            && now - _lastResumeRefreshAt.Value < ResumeRefreshMinimumInterval)
            return;

        _lastResumeRefreshAt = now;
        await RefreshDashboardAsync(false);
    }

    [RelayCommand]
    private Task RefreshAsync() => RefreshDashboardAsync(false);

    private async Task RefreshDashboardAsync(bool allowDuringOperation)
    {
        if (!allowDuringOperation && (IsBusy || _isOperationInProgress))
            return;

        var refreshStartedAt = Stopwatch.GetTimestamp();
        CancelInventoryLoad(true);
        IsBusy = true;
        StatusIsError = false;
        StatusMessage = "Updating";

        try
        {
            var profileStartedAt = Stopwatch.GetTimestamp();
            var profileSnapshot = await _dashboardService.LoadDashboardProfileAsync();
            TracePerf("RefreshProfile", profileStartedAt);
            _lastProfileSnapshot = profileSnapshot;
            ApplyProfileSnapshot(profileSnapshot);
            HasLoadedSnapshot = true;
            StatusMessage = !string.IsNullOrWhiteSpace(profileSnapshot.StatusMessage)
                ? profileSnapshot.StatusMessage
                : IsSupported
                    ? "Updated"
                    : "NotSupported";
            StatusIsError = WorkProfileRecovery == WorkProfileRecoveryKind.UpdateFailedDeleteWorkProfile;

            if (IsDashboardVisible)
            {
                StartInventoryLoad(
                    profileSnapshot,
                    showProgress: SelectedSection == DashboardSection.Apps);
            }
            else
            {
                ApplyInventorySnapshot(DashboardAppInventorySnapshot.Empty);
                HasLoadedInventory = true;
            }
        }
        catch (Exception ex)
        {
            HasLoadedSnapshot = true;
            HasLoadedInventory = true;
            StatusIsError = true;
            StatusMessage = ResolveExceptionMessage(ex, "UpdateState");
            IsInventoryLoading = false;
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
                TracePerf("RefreshDashboard", refreshStartedAt);
            }
        }
    }

    [RelayCommand]
    private void SelectPersonal() => SelectedProfile = ProfileKind.Personal;

    [RelayCommand]
    private void SelectWork()
    {
        if (WorkProfileAvailable) SelectedProfile = ProfileKind.Work;
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
        if (CanOpenSettingsSection) SelectedSection = DashboardSection.Settings;
    }

    [RelayCommand]
    private async Task OpenLogsAsync()
    {
        if (!LoggingEnabled)
            return;

        await ReloadPlatformLogsAsync(true);
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
    private async Task OpenDocumentsUiAsync()
    {
        if (!TryBeginOperation()) return;

        try
        {
            var result = await _settingsService.OpenDocumentsUiAsync();
            StatusIsError = !result.Succeeded;
            StatusMessage = string.IsNullOrWhiteSpace(result.Message)
                ? "DocumentsUiOpened"
                : result.Message;
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = ResolveExceptionMessage(ex, "DocumentsUiOpenFailed");
        }
        finally
        {
            EndOperation();
        }
    }

    [RelayCommand]
    private void ClosePermissions() => IsPermissionsWindowOpen = false;

    [RelayCommand]
    private void CloseAppControlWindow() => CloseAppControl();

    internal void OpenAppControl(AppItemViewModel app)
    {
        SelectedApp = app;
        IsAppControlWindowOpen = true;
    }

    internal void CloseAppControl()
    {
        IsAppControlWindowOpen = false;
        SelectedApp = null;
    }

    [RelayCommand]
    private void SelectAgnosiaTheme() => SelectedTheme = AppThemeKind.Agnosia;

    [RelayCommand]
    private void SelectDarkTheme() => SelectedTheme = AppThemeKind.Dark;

    [RelayCommand]
    private void SelectLightTheme() => SelectedTheme = AppThemeKind.Light;

    [RelayCommand]
    private async Task StartOnboardingAsync()
    {
        if (!WorkProfileAvailable)
        {
            OnboardingStep = OnboardingStep.WorkProfile;
            return;
        }

        SetPreparingOnboardingPermissions(true);
        try
        {
            await ReloadPermissionsAsync();
            OnboardingStep = OnboardingStep.Permissions;
        }
        finally
        {
            SetPreparingOnboardingPermissions(false);
        }
    }

    [RelayCommand]
    private async Task CheckOnboardingWorkProfileAsync()
    {
        await RefreshAsync();
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

        if (OnboardingStep != OnboardingStep.Final)
        {
            StatusIsError = false;
            OnboardingStep = OnboardingStep.Final;
            return;
        }

        await CompleteOnboardingAsync();
        if (OnboardingCompleted && CanOpenAppsSection) SelectedSection = DashboardSection.Apps;
    }

    [RelayCommand(CanExecute = nameof(CanStartProvisioning))]
    private async Task StartProvisioningAsync()
    {
        await RunOperationAsync(
            () => _onboardingService.StartProvisioningAsync(),
            "ProvisioningStarted",
            true,
            true);
        StartOnboardingMonitorIfNeeded();
    }

    [RelayCommand]
    private async Task OpenWorkProfileSettingsAsync()
    {
        if (!TryBeginOperation()) return;

        IsBusy = true;
        try
        {
            var result = await _onboardingService.OpenWorkProfileSettingsAsync();
            StatusIsError = !result.Succeeded;
            StatusMessage = string.IsNullOrWhiteSpace(result.Message) ? "WorkProfileSettingsOpened" : result.Message;
            if (result.Succeeded) MoveToOnboardingStart();
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = ResolveExceptionMessage(ex, "WorkProfileSettingsOpened");
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
                EndOperation();
                _settingsSaveCoordinator.TryStartQueued();
            }
        }
    }

    [RelayCommand]
    private void DismissWorkProfileRecovery() => WorkProfileRecoveryDismissed = true;

    [RelayCommand]
    private void RestartOnboardingFromWorkProfileRecovery() => MoveToOnboardingStart();

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

    internal Task CloneAsync(AppItemViewModel app)
    {
        return RunAppOperationAsync(
            app,
            snapshot => _appCommandService.CloneAsync(snapshot),
            app.Profile == ProfileKind.Personal
                ? "CopyPersonalToWorkFinished"
                : "CopyWorkToPersonalFinished");
    }

    internal async Task MoveToWorkAsync(AppItemViewModel app)
    {
        if (!app.CanMoveToWork || !TryBeginOperation()) return;

        var shouldRefresh = false;

        try
        {
            var snapshot = app.Snapshot;
            var cloneResult = await _appCommandService.CloneAsync(snapshot);
            if (!cloneResult.Succeeded)
            {
                StatusIsError = true;
                StatusMessage = ResolveOperationMessage(cloneResult.Message, "CloneFailed");
                if (IsStaleInstallSourceMessage(cloneResult.Message))
                    await RefreshAfterStaleInstallSourceAsync(StatusMessage);

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
                try
                {
                    var operationStatusIsError = StatusIsError;
                    var operationStatusMessage = StatusMessage;
                    await RefreshDashboardAsync(true);
                    StatusIsError = operationStatusIsError;
                    StatusMessage = operationStatusMessage;
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

    internal Task UninstallAsync(AppItemViewModel app) => RunAppOperationAsync(app, snapshot => _appCommandService.UninstallAsync(snapshot), "Deleted");

    internal Task ToggleFrozenAsync(AppItemViewModel app)
    {
        return RunAppOperationAsync(
            app,
            snapshot => _appCommandService.SetFrozenAsync(snapshot, !snapshot.IsHidden),
            app.IsHidden ? "Restored" : "Hidden");
    }

    internal Task ForceFreezeAsync(AppItemViewModel app) => RunAppOperationAsync(app, snapshot => _appCommandService.ForceFreezeAsync(snapshot), "ForceHidden");

    internal Task CreateShortcutAsync(AppItemViewModel app)
    {
        return RunAppOperationAsync(app, snapshot => _appCommandService.CreateShortcutAsync(snapshot),
            "ShortcutRequested");
    }

    internal Task LaunchAsync(AppItemViewModel app) => RunAppOperationAsync(app, snapshot => _appCommandService.LaunchAsync(snapshot), "Launching");

    internal Task ToggleInteractionAccessAsync(AppItemViewModel app)
    {
        return RunAppOperationAsync(
            app,
            snapshot => _appCommandService.SetInteractionAccessAsync(snapshot, !snapshot.InteractionAllowed),
            app.InteractionAllowed ? "InteractionDisabled" : "InteractionEnabled");
    }

    internal Task RevokeRuntimePermissionsAsync(AppItemViewModel app)
    {
        if (!app.CanRevokeRuntimePermissions) return Task.CompletedTask;

        return RunAppOperationAsync(
            app,
            snapshot => _appCommandService.RevokeRuntimePermissionsAsync(snapshot),
            "RuntimePermissionsRevoked");
    }

    internal async Task RequestPermissionAsync(PermissionItemViewModel permission)
    {
        if (!permission.CanRequest || !TryBeginOperation()) return;

        try
        {
            var refreshOnResume = ShouldRefreshPermissionOnResume(permission.Kind);
            if (refreshOnResume) _refreshPermissionsOnResume = true;

            var result = await _permissionService.RequestPermissionAsync(permission.Kind);
            StatusIsError = !result.Succeeded;
            StatusMessage = string.IsNullOrWhiteSpace(result.Message)
                ? "PermissionRequestOpened"
                : result.Message;

            if (!refreshOnResume || !result.Succeeded)
            {
                if (!result.Succeeded) _refreshPermissionsOnResume = false;

                await ReloadPermissionsAsync();
                await CompleteOnboardingIfReadyAsync();
            }

            if (permission.Kind == PermissionKind.WorkProfile) StartOnboardingMonitorIfNeeded();
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

    internal async Task OpenAppDetailsSettingsAsync()
    {
        if (!TryBeginOperation()) return;

        try
        {
            var result = await _permissionService.OpenAppDetailsSettingsAsync();
            StatusIsError = !result.Succeeded;
            StatusMessage = string.IsNullOrWhiteSpace(result.Message)
                ? "AppDetailsSettingsOpened"
                : result.Message;
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = ResolveExceptionMessage(ex, "AppDetailsSettingsFailed");
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

    private void ApplyProfileSnapshot(DashboardSnapshot snapshot)
    {
        _isApplyingSnapshot = true;
        try
        {
            var previousRecovery = WorkProfileRecovery;
            IsSupported = snapshot.IsSupported;
            HasSetup = snapshot.HasSetup;
            IsSettingUp = snapshot.IsSettingUp;
            WorkProfileAvailable = snapshot.WorkProfileAvailable;
            WorkProfileState = snapshot.WorkProfileState;
            WorkProfileRecovery = snapshot.WorkProfileRecovery;
            if (WorkProfileRecovery == WorkProfileRecoveryKind.None
                || WorkProfileRecovery != previousRecovery)
                WorkProfileRecoveryDismissed = false;

            if (IsSupported
                && !HasSetup
                && OnboardingCompleted
                && WorkProfileState == WorkProfileStateKind.NoWorkProfile)
            {
                OnboardingCompleted = false;
                OnboardingStep = OnboardingStep.WorkProfile;
            }

            ShowAllApps = snapshot.Settings.ShowAllApps;
            DisableVpnBeforeWorkLaunch = snapshot.Settings.DisableVpnBeforeWorkLaunch;
            CrossProfileFileShuttleEnabled = snapshot.Settings.CrossProfileFileShuttleEnabled;
            EnableVpnAfterWorkFreeze = snapshot.Settings.EnableVpnAfterWorkFreeze;
            VpnAfterWorkFreezeClient = snapshot.Settings.VpnAfterWorkFreezeClient;
            TunguskaAutomationToken = snapshot.Settings.TunguskaAutomationToken;
            LoggingEnabled = snapshot.Settings.LoggingEnabled;
            SelectedTheme = snapshot.Settings.Theme;
            _settingsSaveCoordinator.SetLoadedShowAllApps(snapshot.Settings.ShowAllApps);

            LastRefreshedAt = DateTimeOffset.Now;

            if (!WorkProfileAvailable && SelectedProfile == ProfileKind.Work) SelectedProfile = ProfileKind.Personal;

            NotifyOverviewMetricsChanged();

            EnsureSelectedSectionIsAvailable();
        }
        finally
        {
            _isApplyingSnapshot = false;
        }
    }

    private void ApplyInventorySnapshot(DashboardAppInventorySnapshot snapshot)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var retainedKeys = new HashSet<AppItemKey>();
        _personalApps = UpdateAppItems(snapshot.PersonalApps, retainedKeys);
        _workApps = UpdateAppItems(snapshot.WorkApps, retainedKeys);
        DisposeStaleAppItems(retainedKeys);

        OnPropertyChanged(nameof(PersonalAppsCount));
        OnPropertyChanged(nameof(WorkAppsCount));
        NotifyOverviewMetricsChanged();

        RefreshVisibleApps();
        TracePerf(
            "ApplyInventory",
            startedAt,
            $"personal={_personalApps.Length}; work={_workApps.Length}; cached={_appItemCache.Count}");
    }

    private DashboardAppInventorySnapshot PreserveCurrentWorkAppsOnEmpty(
        DashboardSnapshot profileSnapshot,
        DashboardAppInventorySnapshot inventory)
    {
        if (!profileSnapshot.WorkProfileAvailable
            || inventory.WorkApps.Count > 0
            || _workApps.Length == 0)
            return inventory;

        var preservedWorkApps = new AppSnapshot[_workApps.Length];
        for (var index = 0; index < _workApps.Length; index++)
        {
            preservedWorkApps[index] = _workApps[index].Snapshot;
        }

        return new DashboardAppInventorySnapshot(inventory.PersonalApps, preservedWorkApps);
    }

    private void RefreshVisibleApps() => SetVisibleApps(AppCatalogFilter.FilterVisibleApps(_personalApps, _workApps, SelectedProfile, SearchText));

    private void QueueSearchRefresh() => _searchRefreshDebouncer.Schedule(RefreshVisibleAppsAfterDelayAsync);

    private async Task RefreshVisibleAppsAfterDelayAsync(CancellationToken cancellationToken)
    {
        var personalApps = _personalApps;
        var workApps = _workApps;
        var selectedProfile = SelectedProfile;
        var searchText = SearchText;
        var visibleApps = await Task.Run(
            () => AppCatalogFilter.FilterVisibleApps(personalApps, workApps, selectedProfile, searchText),
            cancellationToken);

        await InvokeOnUiThreadActionAsync(() =>
        {
            if (!cancellationToken.IsCancellationRequested
                && ReferenceEquals(personalApps, _personalApps)
                && ReferenceEquals(workApps, _workApps)
                && selectedProfile == SelectedProfile
                && string.Equals(searchText, SearchText, StringComparison.Ordinal))
                SetVisibleApps(visibleApps);
        }, DispatcherPriority.Background);
    }

    private void SetVisibleApps(AppItemViewModel[] visibleApps)
    {
        var startedAt = Stopwatch.GetTimestamp();
        _visibleApps = visibleApps;
        OnPropertyChanged(nameof(VisibleApps));
        OnPropertyChanged(nameof(IsEmptyStateVisible));
        TracePerf("SetVisibleApps", startedAt, $"count={visibleApps.Length}; profile={SelectedProfile}");
    }

    private void CancelPendingSearchRefresh() => _searchRefreshDebouncer.Cancel();

    private Task RunAppOperationAsync(
        AppItemViewModel app,
        Func<AppSnapshot, Task<OperationResult>> operation,
        string successFallback)
    {
        return RunOperationAsync(() => operation(app.Snapshot), successFallback, false);
    }

    private void EnsureSelectedSectionIsAvailable()
    {
        if (!IsDashboardVisible && SelectedSection != DashboardSection.Overview)
            SelectedSection = DashboardSection.Overview;
    }

    private void MoveToOnboardingStart()
    {
        OnboardingCompleted = false;
        OnboardingStep = OnboardingStep.Welcome;
        WorkProfileRecoveryDismissed = true;
    }

    private async Task RunOperationAsync(
        Func<Task<OperationResult>> operation,
        string successFallback,
        bool useBusyIndicator,
        bool refreshOnFailure = false)
    {
        if (!TryBeginOperation()) return;

        if (useBusyIndicator) IsBusy = true;

        try
        {
            var result = await operation();
            StatusIsError = !result.Succeeded;
            StatusMessage = string.IsNullOrWhiteSpace(result.Message) ? successFallback : result.Message;

            if (result.Succeeded)
                await RefreshDashboardAsync(true);
            else if (refreshOnFailure)
            {
                await RefreshDashboardAsync(true);
                StatusIsError = true;
                StatusMessage = string.IsNullOrWhiteSpace(result.Message) ? successFallback : result.Message;
            }
            else if (IsStaleInstallSourceMessage(result.Message))
                await RefreshAfterStaleInstallSourceAsync(StatusMessage);
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
                if (useBusyIndicator) IsBusy = false;

                EndOperation();
                _settingsSaveCoordinator.TryStartQueued();
            }
        }
    }

    private async Task RefreshAfterStaleInstallSourceAsync(string statusMessage)
    {
        await RefreshDashboardAsync(true);
        StatusIsError = true;
        StatusMessage = statusMessage;
    }

    private static bool IsStaleInstallSourceMessage(string? message) => message?.Contains(StaleApkMessageMarker, StringComparison.Ordinal) == true;

    private static bool IsRequiredOnboardingPermission(PermissionKind kind)
    {
        return RequiredOnboardingPermissionKinds.Contains(kind);
    }

    private static bool ShouldRefreshPermissionOnResume(PermissionKind kind)
    {
        return kind is PermissionKind.Notifications
            or PermissionKind.UsageStats
            or PermissionKind.PackageInstall
            or PermissionKind.PersonalAllFiles
            or PermissionKind.WorkAllFiles
            or PermissionKind.Overlay;
    }

    private ValueTask InvokeOnUiThreadActionAsync(
        Action action,
        DispatcherPriority priority = default)
    {
        return _invokeOnUiThreadAsync(
            action,
            priority == default ? DispatcherPriority.Background : priority);
    }

    private static async ValueTask InvokeOnAvaloniaUiThreadAsync(
        Action action,
        DispatcherPriority priority)
    {
        await Dispatcher.UIThread.InvokeAsync(action, priority);
    }

    private bool TryBeginOperation()
    {
        if (IsBusy || _isOperationInProgress) return false;

        _isOperationInProgress = true;
        OnPropertyChanged(nameof(CanStartProvisioning));
        OnPropertyChanged(nameof(IsOperationActive));
        StartProvisioningCommand.NotifyCanExecuteChanged();
        return true;
    }

    private void EndOperation()
    {
        if (!_isOperationInProgress) return;

        _isOperationInProgress = false;
        OnPropertyChanged(nameof(CanStartProvisioning));
        OnPropertyChanged(nameof(IsOperationActive));
        StartProvisioningCommand.NotifyCanExecuteChanged();
    }

    private AppSettingsSnapshot CaptureSettingsSnapshot()
    {
        return new AppSettingsSnapshot(
            ShowAllApps,
            DisableVpnBeforeWorkLaunch,
            CrossProfileFileShuttleEnabled,
            LoggingEnabled,
            SelectedTheme,
            EnableVpnAfterWorkFreeze,
            VpnAfterWorkFreezeClient,
            TunguskaAutomationToken);
    }

    private void QueueSettingsSave() => _settingsSaveCoordinator.Queue();

    internal bool IsVpnAfterFreezeClientSelected(VpnAutomationClientKind kind)
    {
        return VpnAfterWorkFreezeClient == kind;
    }

    internal void SelectVpnAfterFreezeClient(VpnAutomationClientKind kind)
    {
        VpnAfterWorkFreezeClient = kind;
    }

    private VpnAutomationClientOptionViewModel[] CreateVpnAfterFreezeClientOptions()
    {
        return
        [
            new(this, VpnAutomationClientKind.FlClash, "FlClash"),
            new(this, VpnAutomationClientKind.ClashMeta, "Clash Meta"),
            new(this, VpnAutomationClientKind.Happ, "Happ"),
            new(this, VpnAutomationClientKind.Tunguska, "Tunguska"),
            new(this, VpnAutomationClientKind.Incy, "INCY"),
            new(this, VpnAutomationClientKind.Exclave, "Exclave"),
            new(this, VpnAutomationClientKind.Husi, "husi"),
            new(this, VpnAutomationClientKind.NekoBoxPlus, "NekoBox+")
        ];
    }

    private void SetSettingsSaveStatus(bool isError, string? message)
    {
        StatusIsError = isError;
        if (!string.IsNullOrWhiteSpace(message)) StatusMessage = message;
    }

    private void NotifyLogStateChanged()
    {
        OnPropertyChanged(nameof(LogSummary));
        OnPropertyChanged(nameof(LogOutput));
        OnPropertyChanged(nameof(LogLines));
    }

    private async Task ReloadPlatformLogsAsync(bool force = false)
    {
        if (!LoggingEnabled || (!force && !IsLogWindowOpen)) return;

        var startedAt = Stopwatch.GetTimestamp();
        var logs = await _platformEventLogReader.LoadRecentLogsAsync();
        ImportPlatformLogs(logs);
        TracePerf("ReloadPlatformLogs", startedAt, $"count={logs.Count}");
    }

    private Task ReloadPermissionsAsync()
    {
        lock (_permissionReloadSync)
        {
            if (_permissionReloadTask is { IsCompleted: false }) return _permissionReloadTask;

            _permissionReloadTask = ReloadPermissionsCoreAsync();
            return _permissionReloadTask;
        }
    }

    private async Task ReloadPermissionsCoreAsync()
    {
        var snapshots = await _permissionService.LoadPermissionsAsync();

        await InvokeOnUiThreadActionAsync(() =>
        {
            _permissionItems.Clear();
            foreach (var snapshot in snapshots) _permissionItems.Add(new PermissionItemViewModel(this, snapshot));

            OnPropertyChanged(nameof(PermissionSummary));
            OnPropertyChanged(nameof(AreOnboardingPermissionsGranted));
            OnPropertyChanged(nameof(IsOnboardingPermissionsStep));
            OnPropertyChanged(nameof(OnboardingStepLabel));
        }, DispatcherPriority.Background);
    }

    private async Task RefreshPermissionsAfterResumeAsync()
    {
        if (!_initialized
            || (!IsPermissionsWindowOpen && !IsOnboardingPermissionsStep))
            return;

        try
        {
            await ReloadPermissionsAsync();
            await CompleteOnboardingIfReadyAsync();
        }
        catch (Exception ex)
        {
            await ReportErrorOnUiThreadAsync(ex, "PermissionRequestFailed");
        }
    }

    private void ImportPlatformLogs(IEnumerable<AppLogEntry> logs)
    {
        if (_eventLogService.ImportPlatformLogs(logs)) NotifyLogStateChanged();
    }

    private async Task ReportErrorOnUiThreadAsync(Exception exception, string fallbackMessage)
    {
        await InvokeOnUiThreadActionAsync(() =>
        {
            StatusIsError = true;
            StatusMessage = ResolveExceptionMessage(exception, fallbackMessage);
        }, DispatcherPriority.Background);
    }

    private static string ResolveOperationMessage(string? message, string fallback)
    {
        return string.IsNullOrWhiteSpace(message) ? fallback : message;
    }

    private static string ResolveExceptionMessage(Exception _, string fallback)
    {
        return fallback;
    }

    private static string GetAppVersion()
    {
        return typeof(DashboardWorkspaceViewModel)
            .Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            .Split('+')[0] ?? "0.9";
    }

    private AppItemViewModel[] UpdateAppItems(
        IReadOnlyList<AppSnapshot> snapshots,
        HashSet<AppItemKey> retainedKeys)
    {
        var result = new AppItemViewModel[snapshots.Count];
        for (var index = 0; index < snapshots.Count; index++)
        {
            var snapshot = snapshots[index];
            var key = new AppItemKey(snapshot.Profile, snapshot.PackageName);
            retainedKeys.Add(key);
            if (_appItemCache.TryGetValue(key, out var app))
            {
                app.ApplySnapshot(snapshot);
            }
            else
            {
                app = new AppItemViewModel(this, snapshot);
                _appItemCache[key] = app;
            }

            result[index] = app;
        }

        return result;
    }

    private void DisposeStaleAppItems(HashSet<AppItemKey> retainedKeys)
    {
        List<AppItemKey>? staleKeys = null;
        foreach (var key in _appItemCache.Keys)
        {
            if (retainedKeys.Contains(key)) continue;

            staleKeys ??= [];
            staleKeys.Add(key);
        }

        if (staleKeys is null) return;

        foreach (var staleKey in staleKeys)
        {
            var staleApp = _appItemCache[staleKey];
            if (ReferenceEquals(SelectedApp, staleApp)) CloseAppControl();

            staleApp.Dispose();
            _appItemCache.Remove(staleKey);
        }
    }

    private void CancelVisibleIconLoads()
    {
        foreach (var app in _visibleApps) app.CancelIconLoad();
    }

    private void NotifyOverviewMetricsChanged()
    {
        OnPropertyChanged(nameof(TotalManagedAppsCount));
        OnPropertyChanged(nameof(HiddenWorkAppsCount));
        OnPropertyChanged(nameof(InteractionAccessAppsCount));
    }

    private static void TracePerf(string operation, long startedAt, string? detail = null)
    {
        var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $"; {detail}";
        Trace.WriteLine($"AgnosiaPerf {operation} elapsedMs={elapsedMs:0.0}{suffix}");
    }
}
