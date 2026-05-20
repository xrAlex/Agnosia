namespace Agnosia.Models;

public static class AppPermissionRiskCatalog
{
    private static readonly HashSet<string> CriticalPermissions = new(StringComparer.Ordinal)
    {
        "android.permission.ACCESS_BACKGROUND_LOCATION",
        "android.permission.ACCESS_FINE_LOCATION",
        "android.permission.RECORD_AUDIO",
        "android.permission.CAMERA",
        "android.permission.READ_SMS",
        "android.permission.RECEIVE_SMS",
        "android.permission.RECEIVE_MMS",
        "android.permission.RECEIVE_WAP_PUSH",
        "android.permission.SEND_SMS",
        "android.permission.READ_CALL_LOG",
        "android.permission.WRITE_CALL_LOG",
        "android.permission.PROCESS_OUTGOING_CALLS",
        "android.permission.READ_PHONE_NUMBERS",
        "android.permission.READ_PHONE_STATE",
        "android.permission.READ_CONTACTS",
        "android.permission.WRITE_CONTACTS",
        "android.permission.GET_ACCOUNTS",
        "android.permission.MANAGE_EXTERNAL_STORAGE",
        "android.permission.READ_EXTERNAL_STORAGE",
        "android.permission.WRITE_EXTERNAL_STORAGE",
        "android.permission.READ_MEDIA_IMAGES",
        "android.permission.READ_MEDIA_VIDEO",
        "android.permission.READ_MEDIA_AUDIO",
        "android.permission.ACCESS_MEDIA_LOCATION",
        "android.permission.PACKAGE_USAGE_STATS",
        "android.permission.QUERY_ALL_PACKAGES",
        "android.permission.BIND_ACCESSIBILITY_SERVICE",
        "android.permission.BIND_NOTIFICATION_LISTENER_SERVICE",
        "android.permission.RECEIVE_SENSITIVE_NOTIFICATIONS",
        "android.permission.SYSTEM_ALERT_WINDOW",
        "android.permission.BODY_SENSORS_BACKGROUND",
        "android.permission.FOREGROUND_SERVICE_CAMERA",
        "android.permission.FOREGROUND_SERVICE_MICROPHONE",
        "android.permission.FOREGROUND_SERVICE_LOCATION",
        "android.permission.FOREGROUND_SERVICE_MEDIA_PROJECTION",
        "android.permission.CALL_PHONE",
        "android.permission.ANSWER_PHONE_CALLS",
        "android.permission.ACCEPT_HANDOVER"
    };

    private static readonly HashSet<string> DangerousPermissions = new(StringComparer.Ordinal)
    {
        "android.permission.ACCESS_COARSE_LOCATION",
        "android.permission.READ_CALENDAR",
        "android.permission.WRITE_CALENDAR",
        "android.permission.READ_MEDIA_VISUAL_USER_SELECTED",
        "android.permission.NEARBY_WIFI_DEVICES",
        "android.permission.UWB_RANGING",
        "android.permission.RANGING",
        "android.permission.ACCESS_WIFI_STATE",
        "android.permission.ACCESS_NETWORK_STATE",
        "android.permission.READ_VOICEMAIL",
        "android.permission.RECEIVE_BOOT_COMPLETED",
        "android.permission.REQUEST_IGNORE_BATTERY_OPTIMIZATIONS",
        "android.permission.INTERNET"
    };

    public static AppPermissionRiskLevel Classify(IEnumerable<string>? requestedPermissions)
    {
        return Analyze(requestedPermissions).Level;
    }

    public static AppPermissionRiskAnalysis Analyze(IEnumerable<string>? requestedPermissions)
    {
        if (requestedPermissions is null) return AppPermissionRiskAnalysis.Safe;

        var level = AppPermissionRiskLevel.Safe;
        var riskyPermissions = new List<string>();
        var seenPermissions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var permission in requestedPermissions)
        {
            if (string.IsNullOrWhiteSpace(permission)) continue;

            if (!seenPermissions.Add(permission)) continue;

            if (CriticalPermissions.Contains(permission))
            {
                level = AppPermissionRiskLevel.Critical;
                riskyPermissions.Add(permission);
                continue;
            }

            if (DangerousPermissions.Contains(permission))
            {
                if (level == AppPermissionRiskLevel.Safe) level = AppPermissionRiskLevel.Dangerous;
                riskyPermissions.Add(permission);
            }
        }

        return riskyPermissions.Count == 0
            ? AppPermissionRiskAnalysis.Safe
            : new AppPermissionRiskAnalysis(level, riskyPermissions);
    }
}
