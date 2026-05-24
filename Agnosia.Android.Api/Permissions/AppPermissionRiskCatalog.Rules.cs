using Agnosia.Models;

namespace Agnosia.Android.Api.Permissions;

public static partial class AppPermissionRiskCatalog
{
    private const int Android12Api = 31;
    private const int Android12LApi = 32;
    private const int Android13Api = 33;
    private const int Android14Api = 34;
    private const int Android15Api = 35;
    private const int Android16Api = 36;
    private const int Android17Api = 37;
    private const int LegacyExternalStorageMaxTargetSdk = 29;

    private const string AccessBackgroundLocation = "android.permission.ACCESS_BACKGROUND_LOCATION";
    private const string AccessCoarseLocation = "android.permission.ACCESS_COARSE_LOCATION";
    private const string AccessFineLocation = "android.permission.ACCESS_FINE_LOCATION";
    private const string AccessLocalNetwork = "android.permission.ACCESS_LOCAL_NETWORK";
    private const string AccessMediaLocation = "android.permission.ACCESS_MEDIA_LOCATION";
    private const string AnswerPhoneCalls = "android.permission.ANSWER_PHONE_CALLS";
    private const string BindAccessibilityService = "android.permission.BIND_ACCESSIBILITY_SERVICE";
    private const string BindNotificationListenerService = "android.permission.BIND_NOTIFICATION_LISTENER_SERVICE";
    private const string BindVpnService = "android.permission.BIND_VPN_SERVICE";
    private const string BluetoothConnect = "android.permission.BLUETOOTH_CONNECT";
    private const string BluetoothScan = "android.permission.BLUETOOTH_SCAN";
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
    private const string PostNotifications = "android.permission.POST_NOTIFICATIONS";
    private const string QueryAllPackages = "android.permission.QUERY_ALL_PACKAGES";
    private const string ReadAssistStructureScreenContent = "android.permission.READ_ASSIST_STRUCTURE_SCREEN_CONTENT";
    private const string ReadCallLog = "android.permission.READ_CALL_LOG";
    private const string ReadContacts = "android.permission.READ_CONTACTS";
    private const string ReadExternalStorage = "android.permission.READ_EXTERNAL_STORAGE";
    private const string ReadMediaAudio = "android.permission.READ_MEDIA_AUDIO";
    private const string ReadMediaImages = "android.permission.READ_MEDIA_IMAGES";
    private const string ReadMediaVisualUserSelected = "android.permission.READ_MEDIA_VISUAL_USER_SELECTED";
    private const string ReadMediaVideo = "android.permission.READ_MEDIA_VIDEO";
    private const string ReadPhoneNumbers = "android.permission.READ_PHONE_NUMBERS";
    private const string ReadSms = "android.permission.READ_SMS";
    private const string ReceiveSms = "android.permission.RECEIVE_SMS";
    private const string RecordAudio = "android.permission.RECORD_AUDIO";
    private const string SendSms = "android.permission.SEND_SMS";
    private const string RequestInstallPackages = "android.permission.REQUEST_INSTALL_PACKAGES";
    private const string ScheduleExactAlarm = "android.permission.SCHEDULE_EXACT_ALARM";
    private const string SystemAlertWindow = "android.permission.SYSTEM_ALERT_WINDOW";
    private const string UseExactAlarm = "android.permission.USE_EXACT_ALARM";
    private const string WriteCallLog = "android.permission.WRITE_CALL_LOG";
    private const string WriteExternalStorage = "android.permission.WRITE_EXTERNAL_STORAGE";
    private const string FgsCamera = "camera";
    private const string FgsLocation = "location";
    private const string FgsMediaProjection = "mediaProjection";
    private const string FgsMicrophone = "microphone";
    private const string Nfc = "android.permission.NFC";
    private const string ObservedAssistantScreenContent = "android.observed.AssistantScreenContent";
    private const string ObservedMediaProjection = "android.observed.MediaProjection";
    private const string ObservedVpnControl = "android.observed.VpnControl";
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
        Rule("CR-LOC-BG-01", "location-background", AppPermissionRiskLevel.Critical, [AccessFineLocation, AccessBackgroundLocation, Internet], score: 5),
        Rule("CR-LOC-BG-02", "location-background", AppPermissionRiskLevel.Critical, [AccessCoarseLocation, AccessBackgroundLocation, Internet]),
        Rule("CR-SCR-01", AppPermissionRiskLevel.Critical, [ForegroundServiceMediaProjection], minDeviceSdkVersion: Android14Api, foregroundServiceType: FgsMediaProjection, requiredObservedSignals: [ObservedMediaProjection], requireExfiltrationChannel: true),
        Rule("CR-MIC-PERSIST-01", AppPermissionRiskLevel.Critical, [RecordAudio, BootCompleted, Internet], foregroundServiceType: FgsMicrophone, maxTargetSdkVersion: Android13Api),
        Rule("CR-MIC-PERSIST-02", AppPermissionRiskLevel.Critical, [RecordAudio, IgnoreBatteryOptimizations, Internet], foregroundServiceType: FgsMicrophone),
        Rule("CR-MIC-FGS-LEGACY-01", AppPermissionRiskLevel.Critical, [RecordAudio, Internet], foregroundServiceType: FgsMicrophone, maxDeviceSdkVersion: Android13Api),
        Rule("CR-MIC-FGS-14-01", AppPermissionRiskLevel.Critical, [RecordAudio, ForegroundServiceMicrophone, Internet], minDeviceSdkVersion: Android14Api),
        Rule("CR-CAM-PERSIST-01", AppPermissionRiskLevel.Critical, [Camera, BootCompleted, Internet], foregroundServiceType: FgsCamera, maxTargetSdkVersion: Android14Api),
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
        Rule("CR-PROF-USAGE-PERSIST-01", AppPermissionRiskLevel.Critical, [PackageUsageStats, BootCompleted, ForegroundService, Internet]),
        Rule("CR-PROF-USAGE-PERSIST-02", AppPermissionRiskLevel.Critical, [PackageUsageStats, IgnoreBatteryOptimizations, Internet]),
        Rule("CR-PROF-INVENTORY-01", AppPermissionRiskLevel.Critical, [PackageUsageStats, QueryAllPackages, Internet]),
        Rule("CR-FILE-ALL-PERSIST-01", AppPermissionRiskLevel.Critical, [ManageExternalStorage, BootCompleted, Internet]),
        Rule("CR-FILE-ALL-PERSIST-02", AppPermissionRiskLevel.Critical, [ManageExternalStorage, IgnoreBatteryOptimizations, Internet]),
        Rule("CR-FILE-ALL-MEDIA-LOC-01", AppPermissionRiskLevel.Critical, [ManageExternalStorage, AccessMediaLocation, Internet])
    ];

    private static readonly PermissionCombinationRule[] DangerousRules =
    [
        Rule("SU-LOC-01", "location", AppPermissionRiskLevel.Dangerous, [AccessFineLocation], score: 2, excludedPermissions: [AccessBackgroundLocation]),
        Rule("SU-LOC-02", "location", AppPermissionRiskLevel.Dangerous, [AccessCoarseLocation], score: 1, excludedPermissions: [AccessBackgroundLocation]),
        Rule("SU-LOC-FGS-PERSIST-01", "location-persistent", AppPermissionRiskLevel.Dangerous, [AccessFineLocation, BootCompleted, Internet], score: 4, foregroundServiceType: FgsLocation),
        Rule("SU-LOC-FGS-PERSIST-02", "location-persistent", AppPermissionRiskLevel.Dangerous, [AccessCoarseLocation, BootCompleted, Internet], score: 3, foregroundServiceType: FgsLocation),
        Rule("SU-LOC-FGS-PERSIST-03", "location-persistent", AppPermissionRiskLevel.Dangerous, [AccessFineLocation, IgnoreBatteryOptimizations, Internet], score: 4, foregroundServiceType: FgsLocation),
        Rule("SU-LOC-FGS-PERSIST-04", "location-persistent", AppPermissionRiskLevel.Dangerous, [AccessCoarseLocation, IgnoreBatteryOptimizations, Internet], score: 3, foregroundServiceType: FgsLocation),
        Rule("SU-MIC-01", "microphone", AppPermissionRiskLevel.Dangerous, [RecordAudio], score: 1),
        Rule("SU-MIC-PERSIST-01", "microphone", AppPermissionRiskLevel.Dangerous, [RecordAudio, BootCompleted], score: 3),
        Rule("SU-MIC-PERSIST-02", "microphone", AppPermissionRiskLevel.Dangerous, [RecordAudio, IgnoreBatteryOptimizations], score: 3),
        Rule("SU-CAM-01", "camera", AppPermissionRiskLevel.Dangerous, [Camera], score: 1),
        Rule("SU-CAM-PERSIST-01", "camera", AppPermissionRiskLevel.Dangerous, [Camera, BootCompleted], score: 3),
        Rule("SU-CAM-PERSIST-02", "camera", AppPermissionRiskLevel.Dangerous, [Camera, IgnoreBatteryOptimizations], score: 3),
        Rule("SU-CALL-ID-01", AppPermissionRiskLevel.Dangerous, [ReadPhoneNumbers], score: 4),
        Rule("SU-CALL-STATE-PROF-01", AppPermissionRiskLevel.Dangerous, [ReadPhoneState, QueryAllPackages], score: 4),
        Rule("SU-GRAPH-CONTACTS-01", AppPermissionRiskLevel.Dangerous, [ReadContacts], score: 3),
        Rule("SU-GRAPH-ACCOUNTS-01", AppPermissionRiskLevel.Dangerous, [ReadContacts, GetAccounts, ReadPhoneNumbers], score: 4),
        Rule("SU-NOTIF-01", AppPermissionRiskLevel.Dangerous, [BindNotificationListenerService], score: 4, requireEffectivePermissionsForMatch: true),
        Rule("SU-VPN-01", AppPermissionRiskLevel.Dangerous, [BindVpnService], score: 4, requiredObservedSignals: [ObservedVpnControl], requireEffectivePermissionsForMatch: true),
        Rule("SU-UI-ACC-01", AppPermissionRiskLevel.Dangerous, [BindAccessibilityService], score: 5, requireEffectivePermissionsForMatch: true),
        Rule("SU-UI-OVERLAY-01", AppPermissionRiskLevel.Dangerous, [SystemAlertWindow], score: 4, requireEffectivePermissionsForMatch: true),
        Rule("SU-PROF-USAGE-01", AppPermissionRiskLevel.Dangerous, [PackageUsageStats], score: 5, requireEffectivePermissionsForMatch: true),
        Rule("SU-PROF-INVENTORY-01", AppPermissionRiskLevel.Dangerous, [QueryAllPackages], score: 5),
        Rule("SU-FILE-ALL-01", AppPermissionRiskLevel.Dangerous, [ManageExternalStorage], score: 5),
        Rule("SU-MEDIA-LEGACY-01", AppPermissionRiskLevel.Dangerous, [ReadExternalStorage], score: 2, maxDeviceSdkVersion: Android12LApi),
        Rule("SU-FILE-WRITE-LEGACY-01", AppPermissionRiskLevel.Dangerous, [WriteExternalStorage], score: 2, maxDeviceSdkVersion: Android12LApi, maxTargetSdkVersion: LegacyExternalStorageMaxTargetSdk),
        Rule("SU-MEDIA-IMG-01", "media", AppPermissionRiskLevel.Dangerous, [ReadMediaImages], score: 2, minDeviceSdkVersion: Android13Api, excludedPermissions: [ReadMediaVisualUserSelected]),
        Rule("SU-MEDIA-VID-01", "media", AppPermissionRiskLevel.Dangerous, [ReadMediaVideo], score: 2, minDeviceSdkVersion: Android13Api, excludedPermissions: [ReadMediaVisualUserSelected]),
        Rule("SU-MEDIA-AUD-01", "media", AppPermissionRiskLevel.Dangerous, [ReadMediaAudio], score: 2, minDeviceSdkVersion: Android13Api),
        Rule("SU-MEDIA-PARTIAL-01", "media", AppPermissionRiskLevel.Dangerous, [ReadMediaVisualUserSelected], score: 1, minDeviceSdkVersion: Android14Api),
        Rule("SU-MEDIA-LOC-LEGACY-01", "media-location", AppPermissionRiskLevel.Dangerous, [ReadExternalStorage, AccessMediaLocation], score: 4, maxDeviceSdkVersion: Android12LApi),
        Rule("SU-MEDIA-LOC-IMG-01", "media-location", AppPermissionRiskLevel.Dangerous, [ReadMediaImages, AccessMediaLocation], score: 4, minDeviceSdkVersion: Android13Api),
        Rule("SU-MEDIA-LOC-VID-01", "media-location", AppPermissionRiskLevel.Dangerous, [ReadMediaVideo, AccessMediaLocation], score: 4, minDeviceSdkVersion: Android13Api),
        Rule("SU-NEARBY-BLUETOOTH-01", AppPermissionRiskLevel.Dangerous, [NearbyWifiDevices, BluetoothScan], score: 3, minDeviceSdkVersion: Android13Api),
        Rule("SU-BLUETOOTH-EXFIL-01", AppPermissionRiskLevel.Dangerous, [BluetoothConnect, BluetoothScan], score: 3, minDeviceSdkVersion: Android12Api),
        Rule("SU-PROX-RANGING-01", AppPermissionRiskLevel.Dangerous, [Ranging], score: 4, minDeviceSdkVersion: Android16Api),
        Rule("SU-LAN-16-01", AppPermissionRiskLevel.Dangerous, [NearbyWifiDevices], score: 4, minDeviceSdkVersion: Android16Api, maxDeviceSdkVersion: Android16Api, minTargetSdkVersion: Android16Api),
        Rule("SU-LAN-17-01", AppPermissionRiskLevel.Dangerous, [AccessLocalNetwork], score: 4, minDeviceSdkVersion: Android17Api, minTargetSdkVersion: Android17Api),
        Rule("SU-APK-INSTALL-01", AppPermissionRiskLevel.Dangerous, [RequestInstallPackages, QueryAllPackages], score: 5),
        Rule("SU-PERSIST-ALARM-01", AppPermissionRiskLevel.Dangerous, [ScheduleExactAlarm, BootCompleted], score: 4),
        Rule("SU-PERSIST-ALARM-02", AppPermissionRiskLevel.Dangerous, [UseExactAlarm, BootCompleted], score: 4),
        Rule("SU-NOTIF-OVERLAY-01", AppPermissionRiskLevel.Dangerous, [PostNotifications, SystemAlertWindow], score: 4, requireEffectivePermissionsForMatch: true),
        Rule("SU-ASSIST-SCREEN-01", AppPermissionRiskLevel.Dangerous, [ReadAssistStructureScreenContent], score: 5, minDeviceSdkVersion: Android17Api, requiredObservedSignals: [ObservedAssistantScreenContent], requireEffectivePermissionsForMatch: true),
        Rule("SU-SCR-FGS-01", AppPermissionRiskLevel.Dangerous, [ForegroundServiceMediaProjection], score: 5, minDeviceSdkVersion: Android14Api, foregroundServiceType: FgsMediaProjection)
    ];
}
