using Agnosia.Models;

namespace Agnosia.Android.Api.Permissions;

public static class AppPermissionRiskCatalog
{
    private const int Android10Api = 29;
    private const int Android12Api = 31;
    private const int Android12LApi = 32;
    private const int Android13Api = 33;
    private const int Android14Api = 34;
    private const int Android15Api = 35;
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
    private const string ForegroundServiceMediaProjection = "android.permission.FOREGROUND_SERVICE_MEDIA_PROJECTION";
    private const string ForegroundServiceMicrophone = "android.permission.FOREGROUND_SERVICE_MICROPHONE";
    private const string GetAccounts = "android.permission.GET_ACCOUNTS";
    private const string IgnoreBatteryOptimizations = "android.permission.REQUEST_IGNORE_BATTERY_OPTIMIZATIONS";
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
    private const string ReadPhoneState = "android.permission.READ_PHONE_STATE";
    private const string Ranging = "android.permission.RANGING";

    private static readonly Dictionary<string, string> ForegroundServicePermissionByType =
        new(StringComparer.Ordinal)
        {
            [FgsCamera] = ForegroundServiceCamera,
            [FgsLocation] = ForegroundServiceLocation,
            [FgsMediaProjection] = ForegroundServiceMediaProjection,
            [FgsMicrophone] = ForegroundServiceMicrophone
        };

    private static readonly PermissionCombinationRule[] CriticalRules =
    [
        Rule("CR-LOC-BG-01", AppPermissionRiskLevel.Critical, [AccessFineLocation, AccessBackgroundLocation, Internet]),
        Rule("CR-LOC-BG-02", AppPermissionRiskLevel.Critical, [AccessCoarseLocation, AccessBackgroundLocation, Internet]),
        Rule("CR-SCR-01", AppPermissionRiskLevel.Critical, [Internet], foregroundServiceType: FgsMediaProjection),
        Rule("CR-MIC-PERSIST-01", AppPermissionRiskLevel.Critical, [RecordAudio, BootCompleted, Internet], foregroundServiceType: FgsMicrophone, maxTargetSdkVersion: Android13Api),
        Rule("CR-MIC-PERSIST-02", AppPermissionRiskLevel.Critical, [RecordAudio, IgnoreBatteryOptimizations, Internet], foregroundServiceType: FgsMicrophone),
        Rule("CR-MIC-FGS-LEGACY-01", AppPermissionRiskLevel.Critical, [RecordAudio, Internet], foregroundServiceType: FgsMicrophone, maxDeviceSdkVersion: Android13Api),
        Rule("CR-MIC-FGS-14-01", AppPermissionRiskLevel.Critical, [RecordAudio, ForegroundServiceMicrophone, Internet], minDeviceSdkVersion: Android14Api),
        Rule("CR-CAM-PERSIST-01", AppPermissionRiskLevel.Critical, [Camera, BootCompleted, Internet], foregroundServiceType: FgsCamera, maxTargetSdkVersion: Android13Api),
        Rule("CR-CAM-PERSIST-02", AppPermissionRiskLevel.Critical, [Camera, IgnoreBatteryOptimizations, Internet], foregroundServiceType: FgsCamera),
        Rule("CR-CAM-FGS-LEGACY-01", AppPermissionRiskLevel.Critical, [Camera, Internet], foregroundServiceType: FgsCamera, maxDeviceSdkVersion: Android13Api),
        Rule("CR-CAM-FGS-14-01", AppPermissionRiskLevel.Critical, [Camera, ForegroundServiceCamera, Internet], minDeviceSdkVersion: Android14Api),
        Rule("CR-SMS-SEND-01", AppPermissionRiskLevel.Critical, [ReceiveSms, SendSms, Internet]),
        Rule("CR-SMS-READ-01", AppPermissionRiskLevel.Critical, [ReadSms, Internet]),
        Rule("CR-SMS-RECEIVE-01", AppPermissionRiskLevel.Critical, [ReceiveSms, Internet]),
        Rule("CR-CALL-LOG-WRITE-01", AppPermissionRiskLevel.Critical, [ReadCallLog, WriteCallLog, ReadPhoneNumbers, Internet]),
        Rule("CR-CALL-LOG-01", AppPermissionRiskLevel.Critical, [ReadCallLog, Internet]),
        Rule("CR-CALL-REC-01", AppPermissionRiskLevel.Critical, [AnswerPhoneCalls, RecordAudio, Internet]),
        Rule("CR-UI-ACC-OVERLAY-01", AppPermissionRiskLevel.Critical, [BindAccessibilityService, SystemAlertWindow, Internet]),
        Rule("CR-UI-ACC-01", AppPermissionRiskLevel.Critical, [BindAccessibilityService, Internet]),
        Rule("CR-UI-NOTIF-OVERLAY-01", AppPermissionRiskLevel.Critical, [BindNotificationListenerService, SystemAlertWindow, Internet]),
        Rule("CR-UI-ACC-NOTIF-01", AppPermissionRiskLevel.Critical, [BindAccessibilityService, BindNotificationListenerService, Internet]),
        Rule("CR-VPN-ACC-01", AppPermissionRiskLevel.Critical, [BindVpnService, BindAccessibilityService, Internet]),
        Rule("CR-VPN-NOTIF-01", AppPermissionRiskLevel.Critical, [BindVpnService, BindNotificationListenerService, Internet]),
        Rule("CR-PROF-USAGE-PERSIST-01", AppPermissionRiskLevel.Critical, [PackageUsageStats, BootCompleted, ForegroundService, Internet]),
        Rule("CR-PROF-USAGE-PERSIST-02", AppPermissionRiskLevel.Critical, [PackageUsageStats, IgnoreBatteryOptimizations, Internet]),
        Rule("CR-PROF-INVENTORY-01", AppPermissionRiskLevel.Critical, [PackageUsageStats, QueryAllPackages, Internet]),
        Rule("CR-FILE-ALL-PERSIST-01", AppPermissionRiskLevel.Critical, [ManageExternalStorage, BootCompleted, Internet]),
        Rule("CR-FILE-ALL-PERSIST-02", AppPermissionRiskLevel.Critical, [ManageExternalStorage, IgnoreBatteryOptimizations, Internet]),
        Rule("CR-FILE-ALL-MEDIA-LOC-01", AppPermissionRiskLevel.Critical, [ManageExternalStorage, AccessMediaLocation, Internet]),
        Rule("CR-HEALTH-LEGACY-01", AppPermissionRiskLevel.Critical, [BodySensors, BodySensorsBackground, Internet], minDeviceSdkVersion: Android13Api, maxTargetSdkVersion: Android15Api),
        Rule("CR-HEALTH-16-BG-01", AppPermissionRiskLevel.Critical, [ReadHealthDataInBackground, Internet], minDeviceSdkVersion: Android16Api, minTargetSdkVersion: Android16Api, requiredPermissionPrefixes: [HealthReadPermissionPrefix]),
        Rule("CR-HEALTH-16-HISTORY-01", AppPermissionRiskLevel.Critical, [ReadHealthDataHistory, Internet], minDeviceSdkVersion: Android16Api, minTargetSdkVersion: Android16Api, requiredPermissionPrefixes: [HealthReadPermissionPrefix])
    ];

    private static readonly PermissionCombinationRule[] DangerousRules =
    [
        Rule("SU-LOC-01", AppPermissionRiskLevel.Dangerous, [AccessFineLocation, Internet], excludedPermissions: [AccessBackgroundLocation]),
        Rule("SU-LOC-02", AppPermissionRiskLevel.Dangerous, [AccessCoarseLocation, Internet], excludedPermissions: [AccessBackgroundLocation]),
        Rule("SU-LOC-FGS-PERSIST-01", AppPermissionRiskLevel.Dangerous, [AccessFineLocation, BootCompleted, Internet], foregroundServiceType: FgsLocation),
        Rule("SU-LOC-FGS-PERSIST-02", AppPermissionRiskLevel.Dangerous, [AccessCoarseLocation, BootCompleted, Internet], foregroundServiceType: FgsLocation),
        Rule("SU-LOC-FGS-PERSIST-03", AppPermissionRiskLevel.Dangerous, [AccessFineLocation, IgnoreBatteryOptimizations, Internet], foregroundServiceType: FgsLocation),
        Rule("SU-LOC-FGS-PERSIST-04", AppPermissionRiskLevel.Dangerous, [AccessCoarseLocation, IgnoreBatteryOptimizations, Internet], foregroundServiceType: FgsLocation),
        Rule("SU-MIC-01", AppPermissionRiskLevel.Dangerous, [RecordAudio, Internet]),
        Rule("SU-MIC-PERSIST-01", AppPermissionRiskLevel.Dangerous, [RecordAudio, BootCompleted, Internet]),
        Rule("SU-MIC-PERSIST-02", AppPermissionRiskLevel.Dangerous, [RecordAudio, IgnoreBatteryOptimizations, Internet]),
        Rule("SU-CAM-01", AppPermissionRiskLevel.Dangerous, [Camera, Internet]),
        Rule("SU-CAM-PERSIST-01", AppPermissionRiskLevel.Dangerous, [Camera, BootCompleted, Internet]),
        Rule("SU-CAM-PERSIST-02", AppPermissionRiskLevel.Dangerous, [Camera, IgnoreBatteryOptimizations, Internet]),
        Rule("SU-CALL-ID-01", AppPermissionRiskLevel.Dangerous, [ReadPhoneNumbers, Internet]),
        Rule("SU-CALL-STATE-PROF-01", AppPermissionRiskLevel.Dangerous, [ReadPhoneState, QueryAllPackages, Internet]),
        Rule("SU-GRAPH-CONTACTS-01", AppPermissionRiskLevel.Dangerous, [ReadContacts, Internet]),
        Rule("SU-GRAPH-ACCOUNTS-01", AppPermissionRiskLevel.Dangerous, [ReadContacts, GetAccounts, ReadPhoneNumbers, Internet]),
        Rule("SU-NOTIF-01", AppPermissionRiskLevel.Dangerous, [BindNotificationListenerService, Internet]),
        Rule("SU-VPN-01", AppPermissionRiskLevel.Dangerous, [BindVpnService, Internet]),
        Rule("SU-UI-OVERLAY-01", AppPermissionRiskLevel.Dangerous, [SystemAlertWindow, Internet]),
        Rule("SU-PROF-USAGE-01", AppPermissionRiskLevel.Dangerous, [PackageUsageStats, Internet]),
        Rule("SU-PROF-INVENTORY-01", AppPermissionRiskLevel.Dangerous, [QueryAllPackages, Internet]),
        Rule("SU-FILE-ALL-01", AppPermissionRiskLevel.Dangerous, [ManageExternalStorage, Internet]),
        Rule("SU-MEDIA-LEGACY-01", AppPermissionRiskLevel.Dangerous, [ReadExternalStorage, Internet], maxDeviceSdkVersion: Android12LApi),
        Rule("SU-FILE-WRITE-LEGACY-01", AppPermissionRiskLevel.Dangerous, [WriteExternalStorage, Internet], maxDeviceSdkVersion: Android12LApi, maxTargetSdkVersion: Android10Api),
        Rule("SU-MEDIA-IMG-01", AppPermissionRiskLevel.Dangerous, [ReadMediaImages, Internet], minDeviceSdkVersion: Android13Api),
        Rule("SU-MEDIA-VID-01", AppPermissionRiskLevel.Dangerous, [ReadMediaVideo, Internet], minDeviceSdkVersion: Android13Api),
        Rule("SU-MEDIA-AUD-01", AppPermissionRiskLevel.Dangerous, [ReadMediaAudio, Internet], minDeviceSdkVersion: Android13Api),
        Rule("SU-MEDIA-LOC-LEGACY-01", AppPermissionRiskLevel.Dangerous, [ReadExternalStorage, AccessMediaLocation, Internet], maxDeviceSdkVersion: Android12LApi),
        Rule("SU-MEDIA-LOC-IMG-01", AppPermissionRiskLevel.Dangerous, [ReadMediaImages, AccessMediaLocation, Internet], minDeviceSdkVersion: Android13Api),
        Rule("SU-MEDIA-LOC-VID-01", AppPermissionRiskLevel.Dangerous, [ReadMediaVideo, AccessMediaLocation, Internet], minDeviceSdkVersion: Android13Api),
        Rule("SU-NEARBY-BLUETOOTH-01", AppPermissionRiskLevel.Dangerous, [NearbyWifiDevices, BluetoothScan, Internet], minDeviceSdkVersion: Android13Api),
        Rule("SU-HEALTH-LEGACY-01", AppPermissionRiskLevel.Dangerous, [BodySensors, Internet], maxTargetSdkVersion: Android15Api),
        Rule("SU-HEALTH-16-01", AppPermissionRiskLevel.Dangerous, [Internet], minDeviceSdkVersion: Android16Api, minTargetSdkVersion: Android16Api, requiredPermissionPrefixes: [HealthReadPermissionPrefix]),
        Rule("SU-PROX-RANGING-01", AppPermissionRiskLevel.Dangerous, [Ranging, Internet], minDeviceSdkVersion: Android16Api),
        Rule("SU-LAN-16-01", AppPermissionRiskLevel.Dangerous, [NearbyWifiDevices, Internet], minDeviceSdkVersion: Android16Api, maxDeviceSdkVersion: Android16Api, minTargetSdkVersion: Android16Api),
        Rule("SU-LAN-17-01", AppPermissionRiskLevel.Dangerous, [AccessLocalNetwork, Internet], minDeviceSdkVersion: Android17Api, minTargetSdkVersion: Android17Api)
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

        foreach (var rule in CriticalRules)
        {
            if (!rule.IsMatch(context)) continue;

            return new AppPermissionRiskAnalysis(
                AppPermissionRiskLevel.Critical,
                context.GetRiskyPermissions([rule]));
        }

        var matchedRules = new List<PermissionCombinationRule>();
        foreach (var rule in DangerousRules)
        {
            if (rule.IsMatch(context)) matchedRules.Add(rule);
        }

        if (matchedRules.Count == 0) return AppPermissionRiskAnalysis.Safe;

        return new AppPermissionRiskAnalysis(
            AppPermissionRiskLevel.Dangerous,
            context.GetRiskyPermissions(matchedRules));
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
