namespace Agnosia.Models;

public static class AppPermissionRiskCatalog
{
    private const int Android11Api = 30;
    private const int Android12Api = 31;
    private const int Android12LApi = 32;
    private const int Android13Api = 33;
    private const int Android16Api = 36;
    private const int Android17Api = 37;

    private const string AccessBackgroundLocation = "android.permission.ACCESS_BACKGROUND_LOCATION";
    private const string AccessCoarseLocation = "android.permission.ACCESS_COARSE_LOCATION";
    private const string AccessFineLocation = "android.permission.ACCESS_FINE_LOCATION";
    private const string AccessLocalNetwork = "android.permission.ACCESS_LOCAL_NETWORK";
    private const string AccessMediaLocation = "android.permission.ACCESS_MEDIA_LOCATION";
    private const string AnswerPhoneCalls = "android.permission.ANSWER_PHONE_CALLS";
    private const string BindAccessibilityService = "android.permission.BIND_ACCESSIBILITY_SERVICE";
    private const string BindNotificationListenerService = "android.permission.BIND_NOTIFICATION_LISTENER_SERVICE";
    private const string BindVpnService = "android.permission.BIND_VPN_SERVICE";
    private const string BluetoothScan = "android.permission.BLUETOOTH_SCAN";
    private const string BodySensors = "android.permission.BODY_SENSORS";
    private const string BodySensorsBackground = "android.permission.BODY_SENSORS_BACKGROUND";
    private const string BootCompleted = "android.permission.RECEIVE_BOOT_COMPLETED";
    private const string Camera = "android.permission.CAMERA";
    private const string ForegroundService = "android.permission.FOREGROUND_SERVICE";
    private const string ForegroundServiceCamera = "android.permission.FOREGROUND_SERVICE_CAMERA";
    private const string ForegroundServiceLocation = "android.permission.FOREGROUND_SERVICE_LOCATION";
    private const string ForegroundServiceMediaProjection =
        "android.permission.FOREGROUND_SERVICE_MEDIA_PROJECTION";
    private const string ForegroundServiceMicrophone = "android.permission.FOREGROUND_SERVICE_MICROPHONE";
    private const string GetAccounts = "android.permission.GET_ACCOUNTS";
    private const string IgnoreBatteryOptimizations =
        "android.permission.REQUEST_IGNORE_BATTERY_OPTIMIZATIONS";
    private const string Internet = "android.permission.INTERNET";
    private const string ManageExternalStorage = "android.permission.MANAGE_EXTERNAL_STORAGE";
    private const string NearbyWifiDevices = "android.permission.NEARBY_WIFI_DEVICES";
    private const string PackageUsageStats = "android.permission.PACKAGE_USAGE_STATS";
    private const string QueryAllPackages = "android.permission.QUERY_ALL_PACKAGES";
    private const string ReadCallLog = "android.permission.READ_CALL_LOG";
    private const string ReadContacts = "android.permission.READ_CONTACTS";
    private const string ReadExternalStorage = "android.permission.READ_EXTERNAL_STORAGE";
    private const string ReadHealthDataHistory = "android.permission.health.READ_HEALTH_DATA_HISTORY";
    private const string ReadHealthDataInBackground = "android.permission.health.READ_HEALTH_DATA_IN_BACKGROUND";
    private const string ReadMediaAudio = "android.permission.READ_MEDIA_AUDIO";
    private const string ReadMediaImages = "android.permission.READ_MEDIA_IMAGES";
    private const string ReadMediaVideo = "android.permission.READ_MEDIA_VIDEO";
    private const string ReadPhoneNumbers = "android.permission.READ_PHONE_NUMBERS";
    private const string ReadSms = "android.permission.READ_SMS";
    private const string ReceiveSms = "android.permission.RECEIVE_SMS";
    private const string RecordAudio = "android.permission.RECORD_AUDIO";
    private const string SendSms = "android.permission.SEND_SMS";
    private const string SystemAlertWindow = "android.permission.SYSTEM_ALERT_WINDOW";
    private const string WriteCallLog = "android.permission.WRITE_CALL_LOG";
    private const string WriteExternalStorage = "android.permission.WRITE_EXTERNAL_STORAGE";

    private const string HealthReadPermissionPrefix = "android.permission.health.READ_";

    private const string FgsCamera = "camera";
    private const string FgsLocation = "location";
    private const string FgsMediaProjection = "mediaProjection";
    private const string FgsMicrophone = "microphone";

    private static readonly Dictionary<string, string> ForegroundServicePermissionByType =
        new(StringComparer.Ordinal)
        {
            [FgsCamera] = ForegroundServiceCamera,
            [FgsLocation] = ForegroundServiceLocation,
            [FgsMediaProjection] = ForegroundServiceMediaProjection,
            [FgsMicrophone] = ForegroundServiceMicrophone
        };

    private static readonly PermissionCombinationRule[] Rules =
    [
        Rule("CR-LOC-01", AppPermissionRiskLevel.Critical,
            [AccessFineLocation, AccessBackgroundLocation, Internet]),
        Rule("CR-LOC-02", AppPermissionRiskLevel.Critical,
            [AccessFineLocation, AccessBackgroundLocation, BootCompleted, Internet]),
        Rule("CR-LOC-03", AppPermissionRiskLevel.Critical,
            [AccessFineLocation, AccessBackgroundLocation, IgnoreBatteryOptimizations, Internet],
            foregroundServiceType: FgsLocation),
        Rule("CR-MIC-01", AppPermissionRiskLevel.Critical,
            [RecordAudio, Internet],
            foregroundServiceType: FgsMicrophone),
        Rule("CR-CAM-01", AppPermissionRiskLevel.Critical,
            [Camera, Internet],
            foregroundServiceType: FgsCamera),
        Rule("CR-SCR-01", AppPermissionRiskLevel.Critical,
            [RecordAudio, Internet],
            foregroundServiceType: FgsMediaProjection),
        Rule("CR-FILE-01", AppPermissionRiskLevel.Critical,
            [ManageExternalStorage, Internet]),
        Rule("CR-FILE-02", AppPermissionRiskLevel.Critical,
            [ManageExternalStorage, BootCompleted, Internet]),
        Rule("CR-FILE-03", AppPermissionRiskLevel.Critical,
            [ReadMediaImages, ReadMediaVideo, AccessMediaLocation, Internet],
            minDeviceSdkVersion: Android13Api),
        Rule("CR-SMS-01", AppPermissionRiskLevel.Critical,
            [ReadSms, ReceiveSms, Internet]),
        Rule("CR-SMS-02", AppPermissionRiskLevel.Critical,
            [ReceiveSms, SendSms, Internet]),
        Rule("CR-CALL-01", AppPermissionRiskLevel.Critical,
            [AnswerPhoneCalls, RecordAudio, Internet]),
        Rule("CR-CALL-02", AppPermissionRiskLevel.Critical,
            [ReadCallLog, WriteCallLog, ReadPhoneNumbers, Internet]),
        Rule("CR-UI-01", AppPermissionRiskLevel.Critical,
            [BindAccessibilityService, SystemAlertWindow, Internet]),
        Rule("CR-UI-02", AppPermissionRiskLevel.Critical,
            [BindAccessibilityService, BindNotificationListenerService, Internet]),
        Rule("CR-VPN-01", AppPermissionRiskLevel.Critical,
            [BindVpnService, BindAccessibilityService, Internet]),
        Rule("CR-HEALTH-LEGACY-01", AppPermissionRiskLevel.Critical,
            [BodySensors, BodySensorsBackground, Internet],
            minDeviceSdkVersion: Android13Api,
            maxTargetSdkVersion: Android16Api - 1),
        Rule("CR-HEALTH-16-01", AppPermissionRiskLevel.Critical,
            [ReadHealthDataInBackground, Internet],
            minDeviceSdkVersion: Android16Api,
            minTargetSdkVersion: Android16Api),
        Rule("CR-HEALTH-16-02", AppPermissionRiskLevel.Critical,
            [ReadHealthDataHistory, Internet],
            minDeviceSdkVersion: Android16Api,
            minTargetSdkVersion: Android16Api),

        Rule("SU-LOC-01", AppPermissionRiskLevel.Dangerous,
            [AccessFineLocation, Internet]),
        Rule("SU-LOC-02", AppPermissionRiskLevel.Dangerous,
            [AccessCoarseLocation, Internet]),
        Rule("SU-LOC-03", AppPermissionRiskLevel.Dangerous,
            [AccessFineLocation, BootCompleted, Internet],
            excludedPermissions: [AccessBackgroundLocation]),
        Rule("SU-CAP-01", AppPermissionRiskLevel.Dangerous,
            [RecordAudio, Internet]),
        Rule("SU-CAP-02", AppPermissionRiskLevel.Dangerous,
            [Camera, Internet]),
        Rule("SU-CAP-03", AppPermissionRiskLevel.Dangerous,
            [Camera, RecordAudio, Internet]),
        Rule("SU-MEDIA-01", AppPermissionRiskLevel.Dangerous,
            [ReadMediaImages, Internet],
            minDeviceSdkVersion: Android13Api),
        Rule("SU-MEDIA-02", AppPermissionRiskLevel.Dangerous,
            [ReadMediaVideo, Internet],
            minDeviceSdkVersion: Android13Api),
        Rule("SU-MEDIA-03", AppPermissionRiskLevel.Dangerous,
            [ReadMediaAudio, Internet],
            minDeviceSdkVersion: Android13Api),
        Rule("SU-MEDIA-04", AppPermissionRiskLevel.Dangerous,
            [ReadExternalStorage, Internet],
            maxDeviceSdkVersion: Android12LApi),
        Rule("SU-FILE-LEGACY-01", AppPermissionRiskLevel.Dangerous,
            [WriteExternalStorage, Internet],
            maxDeviceSdkVersion: Android12LApi,
            extraCondition: static context =>
                context.TargetSdkVersion > 0 && context.TargetSdkVersion < Android11Api),
        Rule("SU-GRAPH-01", AppPermissionRiskLevel.Dangerous,
            [ReadContacts, Internet]),
        Rule("SU-GRAPH-02", AppPermissionRiskLevel.Dangerous,
            [ReadContacts, GetAccounts, ReadPhoneNumbers, Internet]),
        Rule("SU-NOTIF-01", AppPermissionRiskLevel.Dangerous,
            [BindNotificationListenerService, Internet]),
        Rule("SU-VPN-01", AppPermissionRiskLevel.Dangerous,
            [BindVpnService, Internet]),
        Rule("SU-PERSIST-01", AppPermissionRiskLevel.Dangerous,
            [BootCompleted, IgnoreBatteryOptimizations, ForegroundService]),
        Rule("SU-PROF-01", AppPermissionRiskLevel.Dangerous,
            [PackageUsageStats, Internet]),
        Rule("SU-PROF-02", AppPermissionRiskLevel.Dangerous,
            [QueryAllPackages, Internet]),
        Rule("SU-PROF-03", AppPermissionRiskLevel.Dangerous,
            [PackageUsageStats, QueryAllPackages, Internet]),
        Rule("SU-PROF-04", AppPermissionRiskLevel.Dangerous,
            [NearbyWifiDevices, BluetoothScan, Internet],
            minDeviceSdkVersion: Android13Api),
        Rule("SU-HEALTH-LEGACY-01", AppPermissionRiskLevel.Dangerous,
            [BodySensors, Internet],
            maxTargetSdkVersion: Android16Api - 1),
        Rule("SU-HEALTH-16-01", AppPermissionRiskLevel.Dangerous,
            [Internet],
            minDeviceSdkVersion: Android16Api,
            minTargetSdkVersion: Android16Api,
            requiredPermissionPrefixes: [HealthReadPermissionPrefix]),
        Rule("SU-LAN-16-01", AppPermissionRiskLevel.Dangerous,
            [NearbyWifiDevices, Internet],
            minDeviceSdkVersion: Android16Api,
            maxDeviceSdkVersion: Android16Api,
            minTargetSdkVersion: Android16Api),
        Rule("SU-LAN-17-01", AppPermissionRiskLevel.Dangerous,
            [AccessLocalNetwork, Internet],
            minDeviceSdkVersion: Android17Api,
            minTargetSdkVersion: Android17Api)
    ];

    public static AppPermissionRiskLevel Classify(IEnumerable<string>? requestedPermissions)
    {
        return Analyze(requestedPermissions).Level;
    }

    public static AppPermissionRiskLevel Classify(AppPermissionRiskInput? input)
    {
        return Analyze(input).Level;
    }

    public static AppPermissionRiskAnalysis Analyze(IEnumerable<string>? requestedPermissions)
    {
        return Analyze(new AppPermissionRiskInput(requestedPermissions));
    }

    public static AppPermissionRiskAnalysis Analyze(AppPermissionRiskInput? input)
    {
        if (input is null) return AppPermissionRiskAnalysis.Safe;

        var context = AnalysisContext.Create(input);
        if (!context.HasAnySignal) return AppPermissionRiskAnalysis.Safe;

        var level = AppPermissionRiskLevel.Safe;
        var matchedRules = new List<PermissionCombinationRule>();
        foreach (var rule in Rules)
        {
            if (!rule.IsMatch(context)) continue;

            if (rule.Level > level) level = rule.Level;
            matchedRules.Add(rule);
        }

        if (matchedRules.Count == 0) return AppPermissionRiskAnalysis.Safe;

        return new AppPermissionRiskAnalysis(level, context.GetRiskyPermissions(matchedRules));
    }

    private static PermissionCombinationRule Rule(
        string id,
        AppPermissionRiskLevel level,
        string[] requiredPermissions,
        int minDeviceSdkVersion = Android12Api,
        int? maxDeviceSdkVersion = null,
        int? minTargetSdkVersion = null,
        int? maxTargetSdkVersion = null,
        string? foregroundServiceType = null,
        string[]? excludedPermissions = null,
        string[]? requiredPermissionPrefixes = null,
        Func<AnalysisContext, bool>? extraCondition = null)
    {
        return new PermissionCombinationRule(
            id,
            level,
            minDeviceSdkVersion,
            maxDeviceSdkVersion,
            minTargetSdkVersion,
            maxTargetSdkVersion,
            requiredPermissions,
            excludedPermissions ?? [],
            requiredPermissionPrefixes ?? [],
            foregroundServiceType,
            extraCondition);
    }

    private sealed record PermissionCombinationRule(
        string Id,
        AppPermissionRiskLevel Level,
        int MinDeviceSdkVersion,
        int? MaxDeviceSdkVersion,
        int? MinTargetSdkVersion,
        int? MaxTargetSdkVersion,
        IReadOnlyList<string> RequiredPermissions,
        IReadOnlyList<string> ExcludedPermissions,
        IReadOnlyList<string> RequiredPermissionPrefixes,
        string? ForegroundServiceType,
        Func<AnalysisContext, bool>? ExtraCondition)
    {
        public bool IsMatch(AnalysisContext context)
        {
            if (context.DeviceSdkVersion < MinDeviceSdkVersion) return false;
            if (MaxDeviceSdkVersion is not null && context.DeviceSdkVersion > MaxDeviceSdkVersion) return false;
            if (MinTargetSdkVersion is not null && context.TargetSdkVersion < MinTargetSdkVersion) return false;
            if (MaxTargetSdkVersion is not null
                && (context.TargetSdkVersion == 0 || context.TargetSdkVersion > MaxTargetSdkVersion))
                return false;
            if (RequiredPermissions.Any(permission => !context.HasPermission(permission))) return false;
            if (ExcludedPermissions.Any(context.HasPermission)) return false;
            if (RequiredPermissionPrefixes.Any(prefix => !context.HasPermissionPrefix(prefix))) return false;
            if (ForegroundServiceType is not null && !context.HasForegroundServiceType(ForegroundServiceType))
                return false;

            return ExtraCondition?.Invoke(context) != false;
        }
    }

    private sealed class AnalysisContext
    {
        private readonly HashSet<string> _permissions;
        private readonly HashSet<string> _foregroundServiceTypes;
        private readonly IReadOnlyList<string> _orderedPermissions;

        private AnalysisContext(
            int deviceSdkVersion,
            int targetSdkVersion,
            IReadOnlyList<string> orderedPermissions,
            HashSet<string> permissions,
            HashSet<string> foregroundServiceTypes)
        {
            DeviceSdkVersion = deviceSdkVersion;
            TargetSdkVersion = targetSdkVersion;
            _orderedPermissions = orderedPermissions;
            _permissions = permissions;
            _foregroundServiceTypes = foregroundServiceTypes;
        }

        public int DeviceSdkVersion { get; }

        public int TargetSdkVersion { get; }

        public bool HasAnySignal => _orderedPermissions.Count > 0 || _foregroundServiceTypes.Count > 0;

        public static AnalysisContext Create(AppPermissionRiskInput input)
        {
            var requestedPermissions = NormalizeDistinct(input.RequestedPermissions);
            var servicePermissions = NormalizeDistinct(input.ServicePermissions);
            var orderedPermissions = new List<string>(requestedPermissions.Count + servicePermissions.Count);
            var permissions = new HashSet<string>(StringComparer.Ordinal);
            AddDistinct(orderedPermissions, permissions, requestedPermissions);
            AddDistinct(orderedPermissions, permissions, servicePermissions);

            return new AnalysisContext(
                NormalizeDeviceSdkVersion(input.DeviceSdkVersion),
                input.TargetSdkVersion,
                orderedPermissions,
                permissions,
                NormalizeDistinct(input.ForegroundServiceTypes).ToHashSet(StringComparer.Ordinal));
        }

        public bool HasPermission(string permission)
        {
            return _permissions.Contains(permission);
        }

        public bool HasPermissionPrefix(string prefix)
        {
            return _orderedPermissions.Any(permission => permission.StartsWith(prefix, StringComparison.Ordinal));
        }

        public bool HasForegroundServiceType(string type)
        {
            return _foregroundServiceTypes.Contains(type)
                   || (ForegroundServicePermissionByType.TryGetValue(type, out var permission)
                       && HasPermission(permission));
        }

        public IReadOnlyList<string> GetRiskyPermissions(IReadOnlyList<PermissionCombinationRule> matchedRules)
        {
            var relevantPermissions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in matchedRules)
            {
                foreach (var permission in rule.RequiredPermissions) relevantPermissions.Add(permission);
                foreach (var prefix in rule.RequiredPermissionPrefixes)
                {
                    foreach (var permission in _orderedPermissions.Where(permission =>
                                 permission.StartsWith(prefix, StringComparison.Ordinal)))
                    {
                        relevantPermissions.Add(permission);
                    }
                }

                if (rule.ForegroundServiceType is null) continue;

                relevantPermissions.Add(ForegroundService);
                if (ForegroundServicePermissionByType.TryGetValue(rule.ForegroundServiceType, out var fgsPermission))
                    relevantPermissions.Add(fgsPermission);
            }

            return _orderedPermissions
                .Where(relevantPermissions.Contains)
                .ToArray();
        }

        private static int NormalizeDeviceSdkVersion(int value)
        {
            return value > 0 ? value : Android12Api;
        }

        private static List<string> NormalizeDistinct(IEnumerable<string>? values)
        {
            if (values is null) return [];

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;

                var trimmed = value.Trim();
                if (seen.Add(trimmed)) result.Add(trimmed);
            }

            return result;
        }

        private static void AddDistinct(
            List<string> target,
            HashSet<string> seen,
            IEnumerable<string> values)
        {
            foreach (var value in values)
            {
                if (seen.Add(value)) target.Add(value);
            }
        }
    }
}
