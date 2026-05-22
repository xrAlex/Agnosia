using Agnosia.Models;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stopwatch = System.Diagnostics.Stopwatch;
using Trace = System.Diagnostics.Trace;

namespace Agnosia.ViewModels;

public partial class AppItemViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan[] IconRetryDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(30)
    ];
    private const string AndroidPermissionPrefix = "android.permission.";
    private const string AndroidHealthPermissionPrefix = "android.permission.health.";
    private static readonly IReadOnlyList<string> EmptyRiskyPermissions = [];
    private static readonly AppPermissionRiskScoreBreakdown EmptyRiskScoreBreakdown =
        AppPermissionRiskScoreBreakdown.Empty;

    private readonly DashboardWorkspaceViewModel _owner;
    private bool _iconLoadRequested;
    private bool _disposed;
    private int _iconLoadAttempts;
    private Bitmap? _icon;
    private CancellationTokenSource? _iconLoadCancellation;
    private CancellationTokenSource? _iconRetryCancellation;
    private string? _riskyPermissionsText;
    private string? _manifestPermissionsText;
    private string? _runtimePermissionsText;
    private string[]? _permissionRiskReasons;

    [ObservableProperty]
    private bool _isPermissionDetailsExpanded;

    public AppItemViewModel(DashboardWorkspaceViewModel owner, AppSnapshot snapshot)
    {
        _owner = owner;
        Snapshot = snapshot;
    }

    public AppSnapshot Snapshot { get; private set; }

    public string PackageName => Snapshot.PackageName;

    public string Label => Snapshot.Label;

    public ProfileKind Profile => Snapshot.Profile;

    public bool IsHidden => Snapshot.IsHidden;

    public bool InteractionAllowed => Snapshot.InteractionAllowed;

    public AppPermissionRiskLevel PermissionRiskLevel => Snapshot.PermissionRiskLevel;

    public IReadOnlyList<string> RiskyPermissions => Snapshot.RiskyPermissions ?? EmptyRiskyPermissions;

    public bool HasRiskyPermissions => RiskyPermissions.Count > 0;

    public string RiskyPermissionsText =>
        _riskyPermissionsText ??= string.Join(", ", RiskyPermissions.Select(FormatPermissionName));

    public IReadOnlyList<string> ManifestPermissions => Snapshot.ManifestPermissions ?? EmptyRiskyPermissions;

    public IReadOnlyList<string> RuntimePermissions => Snapshot.RuntimePermissions ?? EmptyRiskyPermissions;

    public IReadOnlyList<string> MatchedPermissionRiskRuleIds =>
        Snapshot.MatchedPermissionRiskRuleIds ?? EmptyRiskyPermissions;

    public AppPermissionRiskScoreBreakdown PermissionRiskScoreBreakdown =>
        Snapshot.PermissionRiskScoreBreakdown ?? EmptyRiskScoreBreakdown;

    public bool HasManifestPermissions => ManifestPermissions.Count > 0;

    public bool HasRuntimePermissions => RuntimePermissions.Count > 0;

    public bool HasPermissionDetails => HasManifestPermissions || HasRuntimePermissions;

    public bool CanRevokeRuntimePermissions => HasRuntimePermissions && !Snapshot.IsSystem && Profile == ProfileKind.Work;

    public string ManifestPermissionsText =>
        _manifestPermissionsText ??= FormatPermissionList(ManifestPermissions);

    public string RuntimePermissionsText =>
        _runtimePermissionsText ??= FormatPermissionList(RuntimePermissions);

    public string PermissionRiskSummaryText => BuildPermissionRiskSummary();

    public IReadOnlyList<string> PermissionRiskReasons => GetPermissionRiskReasons();

    public bool HasPermissionRiskReasons => GetPermissionRiskReasons().Length > 0;

    public bool IsPermissionRiskSafe => PermissionRiskLevel == AppPermissionRiskLevel.Safe;

    public bool IsPermissionRiskDangerous => PermissionRiskLevel == AppPermissionRiskLevel.Dangerous;

    public bool IsPermissionRiskCritical => PermissionRiskLevel == AppPermissionRiskLevel.Critical;

    public bool ShowPermissionRiskIndicator => !Snapshot.IsSystem;

    public string PermissionRiskTooltip => PermissionRiskLevel switch
    {
        AppPermissionRiskLevel.Critical => PermissionRiskSummaryText,
        AppPermissionRiskLevel.Dangerous => PermissionRiskSummaryText,
        _ => "Разрешения: OK"
    };

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
            if (ReferenceEquals(_icon, value)) return;

            var previousIcon = _icon;
            if (SetProperty(ref _icon, value)) previousIcon?.Dispose();
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

    public bool IsAgnosiaManaged => ShowWorkControls && IsHidden;

    public bool CanClone => Profile == ProfileKind.Personal || !Snapshot.IsSystem;

    public bool CanMoveToWork => Profile == ProfileKind.Personal && CanClone && CanUninstall;

    public bool CanUninstall => !Snapshot.IsSystem;

    public bool CanFreeze => Profile == ProfileKind.Work;

    public bool ShowLaunch => Snapshot.CanLaunch || Profile == ProfileKind.Work;

    public string LaunchLabel => Profile == ProfileKind.Work && IsHidden ? "UnfreezeAndOpen" : "Open";

    public string CloneLabel => Profile == ProfileKind.Work ? "CopyToPersonal" : "CopyToWork";

    public static string MoveToWorkLabel => "MoveToWork";

    public static string AgnosiaIsolationLabel => "AgnosiaIsolation";

    public static string ForceFreezeLabel => "ForceFreeze";

    public static string CreateShortcutLabel => "CreateShortcut";

    public static string UninstallLabel => "Uninstall";

    public string InteractionLabel => InteractionAllowed ? "DisallowInteraction" : "AllowInteraction";

    [RelayCommand(CanExecute = nameof(CanClone))]
    private Task CloneAsync()
    {
        return _owner.CloneAsync(this);
    }

    [RelayCommand(CanExecute = nameof(CanMoveToWork))]
    private Task MoveToWorkAsync()
    {
        return _owner.MoveToWorkAsync(this);
    }

    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private Task UninstallAsync()
    {
        return _owner.UninstallAsync(this);
    }

    [RelayCommand(CanExecute = nameof(CanFreeze))]
    private Task ToggleFrozenAsync()
    {
        return _owner.ToggleFrozenAsync(this);
    }

    [RelayCommand(CanExecute = nameof(CanFreeze))]
    private Task ForceFreezeAsync()
    {
        return _owner.ForceFreezeAsync(this);
    }

    [RelayCommand(CanExecute = nameof(ShowWorkControls))]
    private Task CreateShortcutAsync()
    {
        return _owner.CreateShortcutAsync(this);
    }

    [RelayCommand(CanExecute = nameof(ShowLaunch))]
    private Task LaunchAsync()
    {
        return _owner.LaunchAsync(this);
    }

    [RelayCommand(CanExecute = nameof(ShowWorkControls))]
    private Task ToggleInteractionAccessAsync()
    {
        return _owner.ToggleInteractionAccessAsync(this);
    }

    [RelayCommand(CanExecute = nameof(CanRevokeRuntimePermissions))]
    private Task RevokeRuntimePermissionsAsync()
    {
        return _owner.RevokeRuntimePermissionsAsync(this);
    }

    [RelayCommand]
    private void OpenControls()
    {
        _owner.OpenAppControl(this);
    }

    [RelayCommand]
    private void CloseControls()
    {
        _owner.CloseAppControl();
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _iconLoadCancellation?.Cancel();
        _iconLoadCancellation = null;
        _iconRetryCancellation?.Cancel();
        _iconRetryCancellation = null;
        _icon?.Dispose();
        _icon = null;
    }

    public void ApplySnapshot(AppSnapshot snapshot)
    {
        if (!string.Equals(Snapshot.PackageName, snapshot.PackageName, StringComparison.Ordinal)
            || Snapshot.Profile != snapshot.Profile)
            throw new InvalidOperationException("App item identity cannot be changed.");

        var previous = Snapshot;
        Snapshot = snapshot;

        if (!ByteArraysEqual(previous.IconPng, snapshot.IconPng)) ResetIcon();

        if (!string.Equals(previous.Label, snapshot.Label, StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(Label));
            OnPropertyChanged(nameof(Monogram));
        }

        if (previous.IsHidden != snapshot.IsHidden)
        {
            OnPropertyChanged(nameof(IsHidden));
            OnPropertyChanged(nameof(IsAgnosiaManaged));
            OnPropertyChanged(nameof(StatusTagLabel));
            OnPropertyChanged(nameof(HasStatusTag));
            OnPropertyChanged(nameof(ShowSecondaryRow));
            OnPropertyChanged(nameof(LaunchLabel));
        }

        if (previous.InteractionAllowed != snapshot.InteractionAllowed)
        {
            OnPropertyChanged(nameof(InteractionAllowed));
            OnPropertyChanged(nameof(InteractionLabel));
        }

        if (previous.PermissionRiskLevel != snapshot.PermissionRiskLevel)
        {
            _permissionRiskReasons = null;
            OnPropertyChanged(nameof(PermissionRiskLevel));
            OnPropertyChanged(nameof(IsPermissionRiskSafe));
            OnPropertyChanged(nameof(IsPermissionRiskDangerous));
            OnPropertyChanged(nameof(IsPermissionRiskCritical));
            OnPropertyChanged(nameof(ShowPermissionRiskIndicator));
            OnPropertyChanged(nameof(PermissionRiskTooltip));
            OnPropertyChanged(nameof(PermissionRiskSummaryText));
            OnPropertyChanged(nameof(PermissionRiskReasons));
            OnPropertyChanged(nameof(HasPermissionRiskReasons));
        }

        if (!StringListsEqual(previous.RiskyPermissions, snapshot.RiskyPermissions))
        {
            _riskyPermissionsText = null;
            _permissionRiskReasons = null;
            OnPropertyChanged(nameof(RiskyPermissions));
            OnPropertyChanged(nameof(HasRiskyPermissions));
            OnPropertyChanged(nameof(RiskyPermissionsText));
            OnPropertyChanged(nameof(PermissionRiskTooltip));
            OnPropertyChanged(nameof(PermissionRiskSummaryText));
            OnPropertyChanged(nameof(PermissionRiskReasons));
            OnPropertyChanged(nameof(HasPermissionRiskReasons));
        }

        if (!StringListsEqual(previous.MatchedPermissionRiskRuleIds, snapshot.MatchedPermissionRiskRuleIds)
            || previous.PermissionRiskScore != snapshot.PermissionRiskScore
            || previous.PermissionRiskRawScore != snapshot.PermissionRiskRawScore
            || previous.PermissionRiskConfidence != snapshot.PermissionRiskConfidence
            || previous.PermissionRiskScoreBreakdown != snapshot.PermissionRiskScoreBreakdown)
        {
            _permissionRiskReasons = null;
            OnPropertyChanged(nameof(MatchedPermissionRiskRuleIds));
            OnPropertyChanged(nameof(PermissionRiskScoreBreakdown));
            OnPropertyChanged(nameof(PermissionRiskTooltip));
            OnPropertyChanged(nameof(PermissionRiskSummaryText));
            OnPropertyChanged(nameof(PermissionRiskReasons));
            OnPropertyChanged(nameof(HasPermissionRiskReasons));
        }

        if (!StringListsEqual(previous.ManifestPermissions, snapshot.ManifestPermissions))
        {
            _manifestPermissionsText = null;
            OnPropertyChanged(nameof(ManifestPermissions));
            OnPropertyChanged(nameof(HasManifestPermissions));
            OnPropertyChanged(nameof(HasPermissionDetails));
            OnPropertyChanged(nameof(ManifestPermissionsText));
        }

        if (!StringListsEqual(previous.RuntimePermissions, snapshot.RuntimePermissions))
        {
            _runtimePermissionsText = null;
            OnPropertyChanged(nameof(RuntimePermissions));
            OnPropertyChanged(nameof(HasRuntimePermissions));
            OnPropertyChanged(nameof(HasPermissionDetails));
            OnPropertyChanged(nameof(CanRevokeRuntimePermissions));
            OnPropertyChanged(nameof(RuntimePermissionsText));
        }

        if (previous.IsSystem != snapshot.IsSystem)
        {
            OnPropertyChanged(nameof(CanClone));
            OnPropertyChanged(nameof(CanMoveToWork));
            OnPropertyChanged(nameof(CanUninstall));
            OnPropertyChanged(nameof(ShowPermissionRiskIndicator));
            OnPropertyChanged(nameof(CanRevokeRuntimePermissions));
        }

        if (previous.CanLaunch != snapshot.CanLaunch) OnPropertyChanged(nameof(ShowLaunch));

        if (previous.IsInstalled != snapshot.IsInstalled)
        {
            OnPropertyChanged(nameof(StatusTagLabel));
            OnPropertyChanged(nameof(HasStatusTag));
            OnPropertyChanged(nameof(ShowSecondaryRow));
        }

        NotifyCommandStateChanged();
    }

    public void RequestIconLoad()
    {
        if (_disposed || _iconLoadRequested) return;

        _iconLoadRequested = true;
        _iconLoadAttempts++;
        _iconLoadCancellation = new CancellationTokenSource();
        _ = LoadIconAsync(_iconLoadCancellation);
    }

    public void CancelIconLoad()
    {
        if (_icon is not null) return;

        _iconRetryCancellation?.Cancel();
        _iconRetryCancellation = null;
        _iconLoadCancellation?.Cancel();
        _iconLoadCancellation = null;
        _iconLoadRequested = false;
    }

    private async Task LoadIconAsync(CancellationTokenSource iconLoadCancellation)
    {
        var startedAt = Stopwatch.GetTimestamp();
        Bitmap? decodedIcon = null;
        var iconLoaded = false;
        try
        {
            var iconPng = Snapshot.IconPng is { Length: > 0 } existingIcon
                ? existingIcon
                : await _owner
                    .LoadAppIconPngAsync(Snapshot, iconLoadCancellation.Token)
                    .ConfigureAwait(false);
            if (iconPng is not { Length: > 0 }) return;

            decodedIcon = await Task.Run(
                    () => DecodeIcon(iconPng),
                    iconLoadCancellation.Token)
                .ConfigureAwait(false);
            if (decodedIcon is null) return;

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
                iconLoaded = true;
                _iconLoadAttempts = 0;
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
            TracePerf("IconLoad", startedAt, $"profile={Snapshot.Profile}; package={Snapshot.PackageName}");
            if (!iconLoaded && !iconLoadCancellation.IsCancellationRequested)
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!_disposed && ReferenceEquals(_iconLoadCancellation, iconLoadCancellation))
                    {
                        _iconLoadRequested = false;
                        QueueIconRetry();
                    }
                }, DispatcherPriority.Background);

            iconLoadCancellation.Dispose();
            if (ReferenceEquals(_iconLoadCancellation, iconLoadCancellation)) _iconLoadCancellation = null;
        }
    }

    private void ResetIcon()
    {
        _iconLoadCancellation?.Cancel();
        _iconLoadCancellation = null;
        _iconRetryCancellation?.Cancel();
        _iconRetryCancellation = null;
        _iconLoadRequested = false;
        _iconLoadAttempts = 0;
        Icon = null;
        OnPropertyChanged(nameof(HasIcon));
        OnPropertyChanged(nameof(ShowMonogram));
    }

    private void QueueIconRetry()
    {
        if (_iconLoadAttempts > IconRetryDelays.Length) return;

        _iconRetryCancellation?.Cancel();
        _iconRetryCancellation = new CancellationTokenSource();
        var delay = IconRetryDelays[_iconLoadAttempts - 1];
        _ = RetryIconLoadAsync(delay, _iconRetryCancellation);
    }

    private async Task RetryIconLoadAsync(TimeSpan delay, CancellationTokenSource retryCancellation)
    {
        try
        {
            await Task.Delay(delay, retryCancellation.Token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_disposed && ReferenceEquals(_iconRetryCancellation, retryCancellation))
                    _iconRetryCancellation = null;

                if (!_disposed && _icon is null && !_iconLoadRequested)
                    RequestIconLoad();
            }, DispatcherPriority.Background);
        }
        catch (OperationCanceledException) when (retryCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ReferenceEquals(_iconRetryCancellation, retryCancellation))
                    _iconRetryCancellation = null;
            }, DispatcherPriority.Background);
            retryCancellation.Dispose();
        }
    }

    private void NotifyCommandStateChanged()
    {
        CloneCommand.NotifyCanExecuteChanged();
        MoveToWorkCommand.NotifyCanExecuteChanged();
        UninstallCommand.NotifyCanExecuteChanged();
        ToggleFrozenCommand.NotifyCanExecuteChanged();
        ForceFreezeCommand.NotifyCanExecuteChanged();
        CreateShortcutCommand.NotifyCanExecuteChanged();
        LaunchCommand.NotifyCanExecuteChanged();
        ToggleInteractionAccessCommand.NotifyCanExecuteChanged();
        RevokeRuntimePermissionsCommand.NotifyCanExecuteChanged();
    }

    private static void TracePerf(string operation, long startedAt, string detail)
    {
        var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        Trace.WriteLine($"AgnosiaPerf {operation} elapsedMs={elapsedMs:0.0}; {detail}");
    }

    private static bool ByteArraysEqual(byte[]? left, byte[]? right)
    {
        if (ReferenceEquals(left, right)) return true;

        if (left is null || right is null || left.Length != right.Length) return false;

        return left.AsSpan().SequenceEqual(right);
    }

    private static bool StringListsEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (ReferenceEquals(left, right)) return true;

        var leftCount = left?.Count ?? 0;
        if (leftCount != (right?.Count ?? 0)) return false;

        for (var index = 0; index < leftCount; index++)
        {
            if (!string.Equals(left![index], right![index], StringComparison.Ordinal)) return false;
        }

        return true;
    }

    private static string FormatPermissionName(string permission)
    {
        var trimmed = permission.Trim();
        if (trimmed.StartsWith(AndroidHealthPermissionPrefix, StringComparison.Ordinal))
            return trimmed[AndroidHealthPermissionPrefix.Length..];

        if (trimmed.StartsWith(AndroidPermissionPrefix, StringComparison.Ordinal))
            return trimmed[AndroidPermissionPrefix.Length..];

        return trimmed;
    }

    private static string FormatPermissionList(IReadOnlyList<string> permissions)
    {
        return permissions.Count == 0
            ? "Нет"
            : string.Join(Environment.NewLine, permissions.Select(FormatPermissionName));
    }

    private string BuildPermissionRiskSummary()
    {
        return PermissionRiskLevel switch
        {
            AppPermissionRiskLevel.Critical => "Имеет опасные разрешения",
            AppPermissionRiskLevel.Dangerous => "Повышенный риск по разрешениям",
            _ => "Разрешения: OK"
        };
    }

    private string[] GetPermissionRiskReasons()
    {
        return _permissionRiskReasons ??= BuildRiskReasons();
    }

    private string[] BuildRiskReasons()
    {
        if (PermissionRiskLevel == AppPermissionRiskLevel.Safe) return [];

        var permissions = RiskyPermissions;
        var breakdown = PermissionRiskScoreBreakdown;
        var reasons = new List<string>();

        AddSpecificPermissionReasons(permissions, reasons);

        if (breakdown.PersistenceScore > 0)
            reasons.Add("может запускаться или продолжать работу в фоне");

        if (breakdown.ExfiltrationScore > 0)
            reasons.Add("имеет канал для передачи данных наружу");

        if (breakdown.ControlSurfaceScore > 0)
            reasons.Add("получило доступ к чувствительной системной поверхности");

        if (breakdown.StealthScore > 0)
            reasons.Add("может обходить ограничения фоновой работы");

        if (MatchedPermissionRiskRuleIds.Count > 1)
            reasons.Add($"совпало несколько рискованных правил ({MatchedPermissionRiskRuleIds.Count})");

        return reasons.Count == 0 ? [] : reasons.ToArray();
    }

    private static void AddSpecificPermissionReasons(IReadOnlyList<string> permissions, List<string> reasons)
    {
        if (ContainsAny(permissions, "READ_SMS", "RECEIVE_SMS", "SEND_SMS"))
            reasons.Add("может читать или отправлять SMS");
        if (ContainsAny(permissions, "ANSWER_PHONE_CALLS"))
            reasons.Add("может отвечать на телефонные звонки");
        if (ContainsAny(permissions, "READ_CALL_LOG", "WRITE_CALL_LOG", "READ_PHONE_NUMBERS", "READ_PHONE_STATE"))
            reasons.Add("имеет доступ к звонкам или телефонным данным");
        if (ContainsAny(permissions, "READ_CONTACTS", "GET_ACCOUNTS"))
            reasons.Add("может читать контакты или аккаунты");
        if (ContainsAny(permissions, "ACCESS_FINE_LOCATION", "ACCESS_COARSE_LOCATION", "ACCESS_BACKGROUND_LOCATION"))
            reasons.Add("имеет доступ к геолокации");
        if (ContainsAny(permissions, "ACCESS_MEDIA_LOCATION"))
            reasons.Add("может читать геометки внутри фото и видео");
        if (ContainsAny(permissions, "CAMERA"))
            reasons.Add("может использовать камеру");
        if (ContainsAny(permissions, "FOREGROUND_SERVICE_CAMERA"))
            reasons.Add("может держать камеру активной через foreground service");
        if (ContainsAny(permissions, "RECORD_AUDIO"))
            reasons.Add("может использовать микрофон");
        if (ContainsAny(permissions, "FOREGROUND_SERVICE_MICROPHONE"))
            reasons.Add("может держать микрофон активным через foreground service");
        if (ContainsAny(permissions, "READ_MEDIA_IMAGES", "READ_MEDIA_VIDEO", "READ_MEDIA_AUDIO", "READ_MEDIA_VISUAL_USER_SELECTED", "READ_EXTERNAL_STORAGE", "WRITE_EXTERNAL_STORAGE", "MANAGE_EXTERNAL_STORAGE"))
            reasons.Add("имеет доступ к файлам");
        if (ContainsAny(permissions, "READ_MEDIA_VISUAL_USER_SELECTED"))
            reasons.Add("имеет доступ к выбранным фото или видео");
        if (ContainsAny(permissions, "READ_HEALTH", "READ_HEART", "READ_MEDICAL", "READ_SYMPTOM"))
            reasons.Add("может читать медицинские или фитнес-данные");
        if (ContainsAny(permissions, "BODY_SENSORS"))
            reasons.Add("может читать данные датчиков тела");
        if (ContainsAny(permissions, "BODY_SENSORS_BACKGROUND", "READ_HEALTH_DATA_IN_BACKGROUND"))
            reasons.Add("может читать health-данные в фоне");
        if (ContainsAny(permissions, "READ_HEALTH_DATA_HISTORY"))
            reasons.Add("может читать исторические health-данные");
        if (ContainsAny(permissions, "BIND_ACCESSIBILITY_SERVICE", "SYSTEM_ALERT_WINDOW", "BIND_NOTIFICATION_LISTENER_SERVICE"))
            reasons.Add("может читать данные с экрана, уведомления или показывать окна поверх других приложений");
        if (ContainsAny(permissions, "BIND_ACCESSIBILITY_SERVICE"))
            reasons.Add("может управлять интерфейсом");
        if (ContainsAny(permissions, "BIND_NOTIFICATION_LISTENER_SERVICE"))
            reasons.Add("может читать уведомления");
        if (ContainsAny(permissions, "SYSTEM_ALERT_WINDOW"))
            reasons.Add("может показывать окна поверх других приложений");
        if (ContainsAny(permissions, "BIND_VPN_SERVICE"))
            reasons.Add("может направлять трафик через VPN-сервис");
        if (ContainsAny(permissions, "PACKAGE_USAGE_STATS"))
            reasons.Add("может видеть, какие приложения используются");
        if (ContainsAny(permissions, "QUERY_ALL_PACKAGES"))
            reasons.Add("может видеть список установленных приложений");
        if (ContainsAny(permissions, "FOREGROUND_SERVICE_MEDIA_PROJECTION"))
            reasons.Add("может быть связано с записью экрана");
        if (ContainsAny(permissions, "READ_ASSIST_STRUCTURE_SCREEN_CONTENT"))
            reasons.Add("может получать содержимое экрана через assistant API");
        if (ContainsAny(permissions, "REQUEST_INSTALL_PACKAGES"))
            reasons.Add("может устанавливать APK из внешних источников");
        if (ContainsAny(permissions, "RECEIVE_BOOT_COMPLETED"))
            reasons.Add("может запускаться после перезагрузки устройства");
        if (ContainsAny(permissions, "REQUEST_IGNORE_BATTERY_OPTIMIZATIONS"))
            reasons.Add("может обходить ограничения энергосбережения");
        if (ContainsAny(permissions, "SCHEDULE_EXACT_ALARM", "USE_EXACT_ALARM"))
            reasons.Add("может точно будить приложение по расписанию");
        if (ContainsAny(permissions, "FOREGROUND_SERVICE"))
            reasons.Add("может длительно работать в фоне");
        if (ContainsAny(permissions, "POST_NOTIFICATIONS"))
            reasons.Add("может активно показывать уведомления");
        if (ContainsAny(permissions, "ACCESS_NETWORK_STATE"))
            reasons.Add("может отслеживать состояние сети");
        if (ContainsAny(permissions, "ACCESS_LOCAL_NETWORK"))
            reasons.Add("может обращаться к устройствам в локальной сети");
        if (ContainsAny(permissions, "NEARBY_WIFI_DEVICES"))
            reasons.Add("может видеть окружающие Wi-Fi сети и подключаться к ним");
        if (ContainsAny(permissions, "BLUETOOTH_CONNECT", "BLUETOOTH_SCAN"))
            reasons.Add("может использовать Bluetooth для поиска или обмена с устройствами рядом");
        if (ContainsAny(permissions, "NFC"))
            reasons.Add("может использовать NFC модуль");
        if (ContainsAny(permissions, "RANGING"))
            reasons.Add("может оценивать расстояние до nearby-устройств");
    }

    private static bool ContainsAny(IReadOnlyList<string> permissions, params string[] tokens)
    {
        for (var permissionIndex = 0; permissionIndex < permissions.Count; permissionIndex++)
        {
            var permission = permissions[permissionIndex];
            for (var tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
            {
                if (permission.Contains(tokens[tokenIndex], StringComparison.Ordinal)) return true;
            }
        }

        return false;
    }

    private static string ResolveStatusTagLabel(AppSnapshot snapshot)
    {
        if (snapshot.Profile == ProfileKind.Work && snapshot.IsHidden) return "Isolated";

        if (!snapshot.IsInstalled) return "NotInstalled";

        if (snapshot.IsSystem) return "System";

        return string.Empty;
    }

    private static Bitmap? DecodeIcon(byte[]? iconPng)
    {
        if (iconPng is not { Length: > 0 }) return null;

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
