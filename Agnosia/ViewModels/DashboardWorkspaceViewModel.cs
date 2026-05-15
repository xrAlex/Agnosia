using System.Collections.ObjectModel;
using System.Reflection;
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
    private readonly IAppEventLogService _eventLogService;
    private readonly DebouncedAsyncAction _searchRefreshDebouncer;
    private readonly DashboardSettingsSaveCoordinator _settingsSaveCoordinator;
    private readonly SemaphoreSlim _iconLoadGate = new(1, 1);
    private readonly Lock _iconBatchSync = new();
    private readonly Lock _permissionReloadSync = new();
    private readonly List<PendingIconLoad> _pendingIconLoads = [];
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
    private int _inventoryLoadGeneration;
    private CancellationTokenSource? _inventoryLoadCancellation;
    private CancellationTokenSource? _onboardingMonitorCancellation;

    public IReadOnlyList<AppItemViewModel> VisibleApps => _visibleApps;

    public ReadOnlyObservableCollection<PermissionItemViewModel> PermissionItems { get; }

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
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsAppInventoryProgressVisible))]
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
    [NotifyPropertyChangedFor(nameof(CanStartProvisioning))]
    [NotifyPropertyChangedFor(nameof(CanOpenAppsSection))]
    [NotifyPropertyChangedFor(nameof(CanOpenSettingsSection))]
    [NotifyPropertyChangedFor(nameof(WorkProfileStatusText))]
    [NotifyPropertyChangedFor(nameof(OverviewHeadline))]
    [NotifyPropertyChangedFor(nameof(OverallStatusText))]
    [NotifyPropertyChangedFor(nameof(OverallStatusCaption))]
    [NotifyCanExecuteChangedFor(nameof(StartProvisioningCommand))]
    public partial bool HasSetup { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartProvisioning))]
    [NotifyPropertyChangedFor(nameof(OverviewHeadline))]
    [NotifyCanExecuteChangedFor(nameof(StartProvisioningCommand))]
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
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsPersonalProfileSelected))]
    [NotifyPropertyChangedFor(nameof(IsWorkProfileSelected))]
    private partial ProfileKind SelectedProfile { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsAppInventoryProgressVisible))]
    private partial bool HasLoadedInventory { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsAppInventoryProgressVisible))]
    [NotifyPropertyChangedFor(nameof(OverviewHeadline))]
    [NotifyPropertyChangedFor(nameof(OverallStatusText))]
    [NotifyPropertyChangedFor(nameof(OverallStatusCaption))]
    [NotifyPropertyChangedFor(nameof(IsOperationActive))]
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
    public partial bool BlockContactsSearching { get; set; }

    [ObservableProperty]
    public partial bool DisableVpnBeforeWorkLaunch { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVpnAfterFreezeClientPickerVisible))]
    [NotifyPropertyChangedFor(nameof(IsToggleOnlyVpnAfterFreezeWarningVisible))]
    [NotifyPropertyChangedFor(nameof(IsTunguskaAutomationTokenVisible))]
    public partial bool EnableVpnAfterWorkFreeze { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFlClashVpnAfterFreezeSelected))]
    [NotifyPropertyChangedFor(nameof(IsClashMetaVpnAfterFreezeSelected))]
    [NotifyPropertyChangedFor(nameof(IsHappVpnAfterFreezeSelected))]
    [NotifyPropertyChangedFor(nameof(IsTunguskaVpnAfterFreezeSelected))]
    [NotifyPropertyChangedFor(nameof(IsIncyVpnAfterFreezeSelected))]
    [NotifyPropertyChangedFor(nameof(IsExclaveVpnAfterFreezeSelected))]
    [NotifyPropertyChangedFor(nameof(IsHusiVpnAfterFreezeSelected))]
    [NotifyPropertyChangedFor(nameof(IsNekoBoxPlusVpnAfterFreezeSelected))]
    [NotifyPropertyChangedFor(nameof(IsToggleOnlyVpnAfterFreezeWarningVisible))]
    [NotifyPropertyChangedFor(nameof(IsTunguskaAutomationTokenVisible))]
    private partial VpnAutomationClientKind VpnAfterWorkFreezeClient { get; set; } = VpnAutomationClientKind.FlClash;

    [ObservableProperty]
    public partial string TunguskaAutomationToken { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLogWindowOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PermissionSummary))]
    public partial bool IsPermissionsWindowOpen { get; set; }

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
    [NotifyPropertyChangedFor(nameof(IsWorkProfileRecoveryVisible))]
    [NotifyPropertyChangedFor(nameof(WorkProfileRecoveryTitle))]
    [NotifyPropertyChangedFor(nameof(WorkProfileRecoveryMessage))]
    [NotifyPropertyChangedFor(nameof(WorkProfileStatusText))]
    [NotifyPropertyChangedFor(nameof(OverviewHeadline))]
    [NotifyPropertyChangedFor(nameof(OverallStatusCaption))]
    [NotifyPropertyChangedFor(nameof(CanStartProvisioning))]
    [NotifyCanExecuteChangedFor(nameof(StartProvisioningCommand))]
    private partial WorkProfileStateKind WorkProfileState { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorkProfileRecoveryVisible))]
    [NotifyPropertyChangedFor(nameof(WorkProfileRecoveryTitle))]
    [NotifyPropertyChangedFor(nameof(WorkProfileRecoveryMessage))]
    private partial WorkProfileRecoveryKind WorkProfileRecovery { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkProfileRecoveryMessage))]
    private partial string WorkProfileDiagnosticReason { get; set; } = string.Empty;

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
            WorkProfileAvailable,
            WorkProfileState);

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
            WorkProfileAvailable,
            WorkProfileState);

    public string LogSummary => _eventLogService.Summary;

    public string PermissionSummary =>
        _permissionItems.Count == 0
            ? "NotChecked"
            : $"GrantedCount|{_permissionItems.Count(item => item.IsGranted)}|{_permissionItems.Count}";

    public bool AreOnboardingPermissionsGranted =>
        RequiredOnboardingPermissionKinds.All(kind =>
            _permissionItems.Any(item => item.Kind == kind && item.IsGranted));

    public bool IsWorkProfileRecoveryVisible =>
        WorkProfileRecovery != WorkProfileRecoveryKind.None && !WorkProfileRecoveryDismissed;

    public string WorkProfileRecoveryTitle => WorkProfileRecovery switch
    {
        WorkProfileRecoveryKind.WorkProfileQuietMode => "Рабочий профиль выключен",
        WorkProfileRecoveryKind.WorkProfileUnavailable => "Рабочий профиль недоступен",
        WorkProfileRecoveryKind.WorkProfileCommandTargetUnavailable => "Agnosia не видна в рабочем профиле",
        WorkProfileRecoveryKind.WorkProfileCommandChannelUnavailable => "Рабочий профиль не отвечает",
        WorkProfileRecoveryKind.WorkProfileCreatedButAppNotReady => "Рабочий профиль еще не готов",
        WorkProfileRecoveryKind.AppInstalledInWorkProfileButNotOwner => "Agnosia не владелец профиля",
        WorkProfileRecoveryKind.ForeignProfileOwner => "Рабочим профилем управляет другое приложение",
        WorkProfileRecoveryKind.ErrorUnknownWithDiagnostics => "Состояние профиля неясно",
        _ => "Проблема с рабочим профилем"
    };

    public string WorkProfileRecoveryMessage => WorkProfileRecovery switch
    {
        WorkProfileRecoveryKind.WorkProfileQuietMode =>
            WithDiagnosticReason(
                "Android сообщает, что рабочий профиль находится в режиме паузы. Включите рабочий профиль в быстрых настройках или настройках Android, затем обновите экран."),
        WorkProfileRecoveryKind.WorkProfileUnavailable =>
            WithDiagnosticReason(
                "Android видит профиль в группе пользователя, но не предоставляет его для межпрофильных операций. Включите или разблокируйте рабочий профиль в настройках Android, затем обновите экран."),
        WorkProfileRecoveryKind.WorkProfileCommandTargetUnavailable =>
            WithDiagnosticReason(
                "Рабочий профиль доступен Android, но командная активность Agnosia в нем не находится. Подождите завершения установки/запуска профиля, проверьте что Agnosia установлена в рабочем профиле, затем обновите экран."),
        WorkProfileRecoveryKind.WorkProfileCommandChannelUnavailable =>
            WithDiagnosticReason(
                "Командная активность Agnosia найдена в рабочем профиле, но подтвержденный ping не прошел. Разблокируйте или включите рабочий профиль и обновите экран."),
        WorkProfileRecoveryKind.WorkProfileCreatedButAppNotReady =>
            WithDiagnosticReason(
                "Android видит рабочий профиль, но Agnosia в нем пока не отвечает. Подождите, разблокируйте или включите рабочий профиль и обновите экран."),
        WorkProfileRecoveryKind.AppInstalledInWorkProfileButNotOwner =>
            WithDiagnosticReason(
                "Agnosia ответила из рабочего профиля, но Android сообщил, что она не владелец профиля. Чтобы Agnosia управляла этим профилем, удалите старый рабочий профиль в настройках Android и создайте его заново."),
        WorkProfileRecoveryKind.ForeignProfileOwner =>
            WithDiagnosticReason(
                "Диагностика Android указывает на другого владельца рабочего профиля. Удалите этот рабочий профиль в настройках Android, затем создайте профиль Agnosia заново."),
        WorkProfileRecoveryKind.ErrorUnknownWithDiagnostics =>
            WithDiagnosticReason(
                "Agnosia не смогла надежно определить состояние рабочего профиля. Обновите экран или проверьте рабочий профиль в настройках Android."),
        _ => string.Empty
    };

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

    public bool IsFlClashVpnAfterFreezeSelected => VpnAfterWorkFreezeClient == VpnAutomationClientKind.FlClash;

    public bool IsClashMetaVpnAfterFreezeSelected => VpnAfterWorkFreezeClient == VpnAutomationClientKind.ClashMeta;

    public bool IsHappVpnAfterFreezeSelected => VpnAfterWorkFreezeClient == VpnAutomationClientKind.Happ;

    public bool IsTunguskaVpnAfterFreezeSelected => VpnAfterWorkFreezeClient == VpnAutomationClientKind.Tunguska;

    public bool IsIncyVpnAfterFreezeSelected => VpnAfterWorkFreezeClient == VpnAutomationClientKind.Incy;

    public bool IsExclaveVpnAfterFreezeSelected => VpnAfterWorkFreezeClient == VpnAutomationClientKind.Exclave;

    public bool IsHusiVpnAfterFreezeSelected => VpnAfterWorkFreezeClient == VpnAutomationClientKind.Husi;

    public bool IsNekoBoxPlusVpnAfterFreezeSelected => VpnAfterWorkFreezeClient == VpnAutomationClientKind.NekoBoxPlus;

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
        && !HasSetup
        && !IsSettingUp
        && WorkProfileState == WorkProfileStateKind.NoWorkProfile;

    public bool IsOperationActive => IsBusy || _isOperationInProgress || IsInventoryLoading;

    partial void OnSelectedSectionChanged(DashboardSection value)
    {
        if (value == DashboardSection.Apps)
        {
            _settingsSaveCoordinator.TryStartPendingCatalogRefresh();
            StartInventoryLoadIfNeeded();
            return;
        }

        CancelVisibleIconLoads();
        if (IsInventoryLoading) CancelInventoryLoad(true);
    }

    partial void OnSelectedProfileChanged(ProfileKind value)
    {
        CancelVisibleIconLoads();
        CancelPendingSearchRefresh();
        RefreshVisibleApps();
    }

    partial void OnSearchTextChanged(string value)
    {
        QueueSearchRefresh();
    }

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

    partial void OnShowAllAppsChanged(bool value)
    {
        QueueSettingsSave();
    }

    partial void OnBlockContactsSearchingChanged(bool value)
    {
        QueueSettingsSave();
    }

    partial void OnDisableVpnBeforeWorkLaunchChanged(bool value)
    {
        QueueSettingsSave();
    }

    partial void OnEnableVpnAfterWorkFreezeChanged(bool value)
    {
        QueueSettingsSave();
    }

    partial void OnVpnAfterWorkFreezeClientChanged(VpnAutomationClientKind value)
    {
        QueueSettingsSave();
    }

    partial void OnTunguskaAutomationTokenChanged(string value)
    {
        QueueSettingsSave();
    }

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
        IAppEventLogService eventLogService)
    {
        _dashboardService = dashboardService ?? throw new ArgumentNullException(nameof(dashboardService));
        _platformEventLogReader =
            platformEventLogReader ?? throw new ArgumentNullException(nameof(platformEventLogReader));
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
        if (!_refreshPermissionsOnResume) return;

        _refreshPermissionsOnResume = false;
        _ = RefreshPermissionsAfterResumeAsync();
    }

    [RelayCommand]
    private Task RefreshAsync()
    {
        return RefreshDashboardAsync(false);
    }

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
            StatusMessage = IsSupported
                ? "Updated"
                : "NotSupported";

            if (IsDashboardVisible && SelectedSection == DashboardSection.Apps)
            {
                StartInventoryLoad(profileSnapshot);
            }
            else if (IsDashboardVisible)
            {
                HasLoadedInventory = false;
                IsInventoryLoading = false;
                CancelVisibleIconLoads();
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

    private void StartInventoryLoad(DashboardSnapshot profileSnapshot)
    {
        var generation = BeginInventoryLoad(out var inventoryCancellation);
        HasLoadedInventory = false;
        IsInventoryLoading = true;
        StatusIsError = false;
        StatusMessage = "LoadingApps";
        _ = LoadInventoryForGenerationAsync(profileSnapshot, generation, inventoryCancellation);
    }

    private void StartInventoryLoadIfNeeded()
    {
        if (!IsDashboardVisible
            || HasLoadedInventory
            || IsInventoryLoading
            || _lastProfileSnapshot is null)
            return;

        StartInventoryLoad(_lastProfileSnapshot);
    }

    private int BeginInventoryLoad(out CancellationTokenSource inventoryCancellation)
    {
        CancelInventoryLoad(false);
        inventoryCancellation = new CancellationTokenSource();
        _inventoryLoadCancellation = inventoryCancellation;
        return ++_inventoryLoadGeneration;
    }

    private void CancelInventoryLoad(bool updateProgressState)
    {
        if (_inventoryLoadCancellation is not null)
        {
            _inventoryLoadCancellation.Cancel();
            _inventoryLoadCancellation = null;
        }

        ++_inventoryLoadGeneration;
        if (updateProgressState) IsInventoryLoading = false;
    }

    private async Task LoadInventoryForGenerationAsync(
        DashboardSnapshot profileSnapshot,
        int generation,
        CancellationTokenSource inventoryCancellation)
    {
        try
        {
            var loadStartedAt = Stopwatch.GetTimestamp();
            var inventory = await _dashboardService
                .LoadAppInventoryAsync(profileSnapshot, inventoryCancellation.Token)
                .ConfigureAwait(false);
            TracePerf(
                "LoadInventory",
                loadStartedAt,
                $"personal={inventory.PersonalApps.Count}; work={inventory.WorkApps.Count}");

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsCurrentInventoryLoad(generation, inventoryCancellation)) return;

                ApplyInventorySnapshot(inventory);
                HasLoadedInventory = true;
                IsInventoryLoading = false;
                StatusMessage = IsSupported ? "Updated" : "NotSupported";
            }, DispatcherPriority.Background);
        }
        catch (OperationCanceledException) when (inventoryCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsCurrentInventoryLoad(generation, inventoryCancellation)) return;

                HasLoadedInventory = true;
                IsInventoryLoading = false;
                StatusIsError = true;
                StatusMessage = ResolveExceptionMessage(ex, "LoadAppsFailed");
            }, DispatcherPriority.Background);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ReferenceEquals(_inventoryLoadCancellation, inventoryCancellation))
                    _inventoryLoadCancellation = null;
            }, DispatcherPriority.Background);
            inventoryCancellation.Dispose();
        }
    }

    private bool IsCurrentInventoryLoad(int generation, CancellationTokenSource inventoryCancellation)
    {
        return generation == _inventoryLoadGeneration
               && ReferenceEquals(_inventoryLoadCancellation, inventoryCancellation)
               && !inventoryCancellation.IsCancellationRequested;
    }

    [RelayCommand]
    private void SelectPersonal()
    {
        SelectedProfile = ProfileKind.Personal;
    }

    [RelayCommand]
    private void SelectWork()
    {
        if (WorkProfileAvailable) SelectedProfile = ProfileKind.Work;
    }

    [RelayCommand]
    private void OpenOverviewSection()
    {
        SelectedSection = DashboardSection.Overview;
    }

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
    private void CloseLogs()
    {
        IsLogWindowOpen = false;
    }

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
    private void ClosePermissions()
    {
        IsPermissionsWindowOpen = false;
    }

    [RelayCommand]
    private void SelectAgnosiaTheme()
    {
        SelectedTheme = AppThemeKind.Agnosia;
    }

    [RelayCommand]
    private void SelectDarkTheme()
    {
        SelectedTheme = AppThemeKind.Dark;
    }

    [RelayCommand]
    private void SelectLightTheme()
    {
        SelectedTheme = AppThemeKind.Light;
    }

    [RelayCommand]
    private void SelectFlClashVpnAfterFreeze()
    {
        VpnAfterWorkFreezeClient = VpnAutomationClientKind.FlClash;
    }

    [RelayCommand]
    private void SelectClashMetaVpnAfterFreeze()
    {
        VpnAfterWorkFreezeClient = VpnAutomationClientKind.ClashMeta;
    }

    [RelayCommand]
    private void SelectHappVpnAfterFreeze()
    {
        VpnAfterWorkFreezeClient = VpnAutomationClientKind.Happ;
    }

    [RelayCommand]
    private void SelectTunguskaVpnAfterFreeze()
    {
        VpnAfterWorkFreezeClient = VpnAutomationClientKind.Tunguska;
    }

    [RelayCommand]
    private void SelectIncyVpnAfterFreeze()
    {
        VpnAfterWorkFreezeClient = VpnAutomationClientKind.Incy;
    }

    [RelayCommand]
    private void SelectExclaveVpnAfterFreeze()
    {
        VpnAfterWorkFreezeClient = VpnAutomationClientKind.Exclave;
    }

    [RelayCommand]
    private void SelectHusiVpnAfterFreeze()
    {
        VpnAfterWorkFreezeClient = VpnAutomationClientKind.Husi;
    }

    [RelayCommand]
    private void SelectNekoBoxPlusVpnAfterFreeze()
    {
        VpnAfterWorkFreezeClient = VpnAutomationClientKind.NekoBoxPlus;
    }

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
            true);
        StartOnboardingMonitorIfNeeded();
    }

    [RelayCommand]
    private async Task OpenWorkProfileSettingsAsync()
    {
        await RunOperationAsync(
            () => _onboardingService.OpenWorkProfileSettingsAsync(),
            "WorkProfileSettingsOpened",
            true);
    }

    [RelayCommand]
    private void DismissWorkProfileRecovery()
    {
        WorkProfileRecoveryDismissed = true;
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
                    await RefreshDashboardAsync(true);
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

    internal Task UninstallAsync(AppItemViewModel app)
    {
        return RunAppOperationAsync(app, snapshot => _appCommandService.UninstallAsync(snapshot), "Deleted");
    }

    internal Task ToggleFrozenAsync(AppItemViewModel app)
    {
        return RunAppOperationAsync(
            app,
            snapshot => _appCommandService.SetFrozenAsync(snapshot, !snapshot.IsHidden),
            app.IsHidden ? "Restored" : "Hidden");
    }

    internal Task ForceFreezeAsync(AppItemViewModel app)
    {
        return RunAppOperationAsync(app, snapshot => _appCommandService.ForceFreezeAsync(snapshot), "ForceHidden");
    }

    internal Task CreateShortcutAsync(AppItemViewModel app)
    {
        return RunAppOperationAsync(app, snapshot => _appCommandService.CreateShortcutAsync(snapshot),
            "ShortcutRequested");
    }

    internal Task LaunchAsync(AppItemViewModel app)
    {
        return RunAppOperationAsync(app, snapshot => _appCommandService.LaunchAsync(snapshot), "Launching");
    }

    internal Task ToggleInteractionAccessAsync(AppItemViewModel app)
    {
        return RunAppOperationAsync(
            app,
            snapshot => _appCommandService.SetInteractionAccessAsync(snapshot, !snapshot.InteractionAllowed),
            app.InteractionAllowed ? "InteractionDisabled" : "InteractionEnabled");
    }

    internal async Task<byte[]?> LoadAppIconPngAsync(AppSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (snapshot.IconPng is { Length: > 0 } existingIcon) return existingIcon;

        return await QueueIconLoadAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    private Task<byte[]?> QueueIconLoadAsync(AppSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<byte[]?>(cancellationToken);

        var pendingIconLoad = new PendingIconLoad(snapshot, cancellationToken);
        lock (_iconBatchSync)
        {
            _pendingIconLoads.Add(pendingIconLoad);
            _iconBatchProcessor ??= ProcessIconLoadBatchesAsync();
        }

        return pendingIconLoad.Task;
    }

    private async Task ProcessIconLoadBatchesAsync()
    {
        while (true)
        {
            await Task.Delay(IconBatchDelayMs).ConfigureAwait(false);
            PendingIconLoad[] batch;
            lock (_iconBatchSync)
            {
                if (_pendingIconLoads.Count == 0)
                {
                    _iconBatchProcessor = null;
                    return;
                }

                batch = _pendingIconLoads.ToArray();
                _pendingIconLoads.Clear();
            }

            foreach (var completedRequest in batch.Where(request => request.IsCompleted)) completedRequest.Dispose();

            batch = batch.Where(request => !request.IsCompleted).ToArray();
            if (batch.Length == 0) continue;

            await LoadIconBatchAsync(batch).ConfigureAwait(false);
        }
    }

    private async Task LoadIconBatchAsync(PendingIconLoad[] batch)
    {
        var snapshots = batch
            .Select(request => request.Snapshot)
            .GroupBy(snapshot => (snapshot.Profile, snapshot.PackageName))
            .Select(group => group.First())
            .ToArray();
        var startedAt = Stopwatch.GetTimestamp();

        IReadOnlyDictionary<string, byte[]?> icons;
        await _iconLoadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            icons = await _dashboardService.LoadAppIconsAsync(snapshots).ConfigureAwait(false);
            TracePerf("IconBatchLoad", startedAt, $"requested={snapshots.Length}; completed={icons.Count}");
        }
        catch (Exception exception)
        {
            foreach (var request in batch) request.TrySetException(exception);

            return;
        }
        finally
        {
            _iconLoadGate.Release();
        }

        foreach (var request in batch)
        {
            icons.TryGetValue(request.Snapshot.PackageName, out var iconPng);
            request.TrySetResult(iconPng);
        }
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

    private void ApplySnapshot(DashboardSnapshot snapshot)
    {
        _lastProfileSnapshot = snapshot;
        ApplyProfileSnapshot(snapshot);
        ApplyInventorySnapshot(new DashboardAppInventorySnapshot(snapshot.PersonalApps, snapshot.WorkApps));
        HasLoadedSnapshot = true;
        HasLoadedInventory = true;
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
            WorkProfileDiagnosticReason = snapshot.WorkProfileDiagnosticReason;
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

            LastRefreshedAt = DateTimeOffset.Now;

            if (!WorkProfileAvailable && SelectedProfile == ProfileKind.Work) SelectedProfile = ProfileKind.Personal;

            OnPropertyChanged(nameof(CanOpenAppsSection));
            OnPropertyChanged(nameof(CanOpenSettingsSection));
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

    private void CancelPendingSearchRefresh()
    {
        _searchRefreshDebouncer.Cancel();
    }

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

    private async Task RunOperationAsync(
        Func<Task<OperationResult>> operation,
        string successFallback,
        bool useBusyIndicator)
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

    private static bool IsStaleInstallSourceMessage(string? message)
    {
        return message?.Contains(StaleApkMessageMarker, StringComparison.Ordinal) == true;
    }

    private static bool IsRequiredOnboardingPermission(PermissionKind kind)
    {
        return RequiredOnboardingPermissionKinds.Contains(kind);
    }

    private static bool ShouldRefreshPermissionOnResume(PermissionKind kind)
    {
        return kind is PermissionKind.UsageStats
            or PermissionKind.PackageInstall
            or PermissionKind.Overlay;
    }

    private void StartOnboardingMonitorIfNeeded()
    {
        if (OnboardingCompleted
            || OnboardingStep == OnboardingStep.Welcome
            || OnboardingStep == OnboardingStep.Permissions
            || OnboardingStep == OnboardingStep.Final
            || _isOperationInProgress
            || _onboardingMonitorCancellation is not null)
            return;

        if (OnboardingStep == OnboardingStep.WorkProfile && !IsSettingUp && !WorkProfileAvailable) return;

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
                if (_isOperationInProgress)
                {
                    await Task.Delay(OnboardingMonitorDelayMs, cancellationToken);
                    continue;
                }

                await InvokeOnUiThreadAsync(() => AdvanceOnboardingAsync(cancellationToken));

                if (OnboardingCompleted
                    || OnboardingStep == OnboardingStep.Welcome
                    || OnboardingStep == OnboardingStep.Permissions
                    || OnboardingStep == OnboardingStep.Final)
                    return;

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

    private static async Task InvokeOnUiThreadAsync(Func<Task> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            await action();
            return;
        }

        var completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await action();
                completionSource.TrySetResult();
            }
            catch (Exception exception)
            {
                completionSource.TrySetException(exception);
            }
        }, DispatcherPriority.Background);

        await completionSource.Task;
    }

    private async Task AdvanceOnboardingAsync(CancellationToken cancellationToken)
    {
        if (IsBusy || _isOperationInProgress) return;

        if (OnboardingStep == OnboardingStep.WorkProfile)
        {
            SetPreparingOnboardingPermissions(true);
            try
            {
                await RefreshAsync();
                if (!WorkProfileAvailable)
                {
                    SetPreparingOnboardingPermissions(false);
                    return;
                }

                await ReloadPermissionsAsync();
                OnboardingStep = OnboardingStep.Permissions;
            }
            finally
            {
                SetPreparingOnboardingPermissions(false);
            }
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
            OnboardingStep = OnboardingStep.Final;
    }

    private void SetPreparingOnboardingPermissions(bool value)
    {
        if (_isPreparingOnboardingPermissions == value) return;

        _isPreparingOnboardingPermissions = value;
        OnPropertyChanged(nameof(IsOnboardingWorkProfileStep));
        OnPropertyChanged(nameof(IsOnboardingPermissionsStep));
        OnPropertyChanged(nameof(OnboardingStepLabel));
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
            BlockContactsSearching,
            DisableVpnBeforeWorkLaunch,
            LoggingEnabled,
            SelectedTheme,
            EnableVpnAfterWorkFreeze,
            VpnAfterWorkFreezeClient,
            TunguskaAutomationToken);
    }

    private void QueueSettingsSave()
    {
        _settingsSaveCoordinator.Queue();
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

        await Dispatcher.UIThread.InvokeAsync(() =>
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
        await Dispatcher.UIThread.InvokeAsync(() =>
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

    private string WithDiagnosticReason(string message)
    {
        return string.IsNullOrWhiteSpace(WorkProfileDiagnosticReason)
            ? message
            : $"{message} Причина: {WorkProfileDiagnosticReason}.";
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
        foreach (var staleKey in _appItemCache.Keys.Where(key => !retainedKeys.Contains(key)).ToArray())
        {
            _appItemCache[staleKey].Dispose();
            _appItemCache.Remove(staleKey);
        }
    }

    private void CancelVisibleIconLoads()
    {
        foreach (var app in _visibleApps) app.CancelIconLoad();
    }

    private void NotifyOverviewMetricsChanged()
    {
        OnPropertyChanged(nameof(OverviewHeadline));
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

    private sealed class PendingIconLoad : IDisposable
    {
        private readonly TaskCompletionSource<byte[]?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly CancellationToken _cancellationToken;
        private readonly CancellationTokenRegistration _cancellationRegistration;

        public PendingIconLoad(AppSnapshot snapshot, CancellationToken cancellationToken)
        {
            Snapshot = snapshot;
            _cancellationToken = cancellationToken;
            _cancellationRegistration = cancellationToken.CanBeCanceled
                ? cancellationToken.Register(static state =>
                    ((PendingIconLoad)state!)._completion.TrySetCanceled(
                        ((PendingIconLoad)state)._cancellationToken), this)
                : default;
        }

        public AppSnapshot Snapshot { get; }

        public Task<byte[]?> Task => _completion.Task;

        public bool IsCompleted => _completion.Task.IsCompleted;

        public void TrySetResult(byte[]? iconPng)
        {
            _completion.TrySetResult(iconPng);
            Dispose();
        }

        public void TrySetException(Exception exception)
        {
            _completion.TrySetException(exception);
            Dispose();
        }

        public void Dispose()
        {
            _cancellationRegistration.Dispose();
        }
    }

    private readonly record struct AppItemKey(ProfileKind Profile, string PackageName);
}
