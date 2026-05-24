using Agnosia.Models;

namespace Agnosia.ViewModels;

internal static class AppPermissionRiskTextFormatter
{
    private const string AndroidPermissionPrefix = "android.permission.";
    private const string AndroidHealthPermissionPrefix = "android.permission.health.";

    public static string FormatPermissionInlineList(IReadOnlyList<string> permissions)
    {
        return string.Join(", ", permissions.Select(FormatPermissionName));
    }

    public static string FormatPermissionBlockList(IReadOnlyList<string> permissions)
    {
        return permissions.Count == 0
            ? "Нет"
            : string.Join(Environment.NewLine, permissions.Select(FormatPermissionName));
    }

    public static string BuildRiskSummary(AppPermissionRiskLevel permissionRiskLevel)
    {
        return permissionRiskLevel switch
        {
            AppPermissionRiskLevel.Critical => "Имеет опасные разрешения",
            AppPermissionRiskLevel.Dangerous => "Повышенный риск по разрешениям",
            _ => "Разрешения: OK"
        };
    }

    public static string[] BuildRiskReasons(
        AppPermissionRiskLevel permissionRiskLevel,
        IReadOnlyList<string> riskyPermissions,
        AppPermissionRiskScoreBreakdown breakdown,
        IReadOnlyList<string> matchedPermissionRiskRuleIds)
    {
        if (permissionRiskLevel == AppPermissionRiskLevel.Safe) return [];

        var reasons = new List<string>();

        AddSpecificPermissionReasons(riskyPermissions, reasons);

        if (breakdown.PersistenceScore > 0)
            reasons.Add("может запускаться или продолжать работу в фоне");

        if (breakdown.ExfiltrationScore > 0)
            reasons.Add("имеет канал для передачи данных наружу");

        if (breakdown.ControlSurfaceScore > 0)
            reasons.Add("получило доступ к чувствительной системной поверхности");

        if (breakdown.StealthScore > 0)
            reasons.Add("может обходить ограничения фоновой работы");

        if (matchedPermissionRiskRuleIds.Count > 1)
            reasons.Add($"совпало несколько рискованных правил ({matchedPermissionRiskRuleIds.Count})");

        return reasons.Count == 0 ? [] : reasons.ToArray();
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
}
