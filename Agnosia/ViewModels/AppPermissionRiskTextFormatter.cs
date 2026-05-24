using Agnosia.Models;

namespace Agnosia.ViewModels;

internal static class AppPermissionRiskTextFormatter
{
    private const string AndroidPermissionPrefix = "android.permission.";
    private const string AndroidHealthPermissionPrefix = "android.permission.health.";
    private static readonly PermissionReasonRule[] SpecificPermissionReasonRules =
    [
        new(["READ_SMS", "RECEIVE_SMS", "SEND_SMS"], "может читать или отправлять SMS"),
        new(["ANSWER_PHONE_CALLS"], "может отвечать на телефонные звонки"),
        new(["READ_CALL_LOG", "WRITE_CALL_LOG", "READ_PHONE_NUMBERS", "READ_PHONE_STATE"], "имеет доступ к звонкам или телефонным данным"),
        new(["READ_CONTACTS", "GET_ACCOUNTS"], "может читать контакты или аккаунты"),
        new(["ACCESS_FINE_LOCATION", "ACCESS_COARSE_LOCATION", "ACCESS_BACKGROUND_LOCATION"], "имеет доступ к геолокации"),
        new(["ACCESS_MEDIA_LOCATION"], "может читать геометки внутри фото и видео"),
        new(["CAMERA"], "может использовать камеру"),
        new(["FOREGROUND_SERVICE_CAMERA"], "может держать камеру активной через foreground service"),
        new(["RECORD_AUDIO"], "может использовать микрофон"),
        new(["FOREGROUND_SERVICE_MICROPHONE"], "может держать микрофон активным через foreground service"),
        new(["READ_MEDIA_IMAGES", "READ_MEDIA_VIDEO", "READ_MEDIA_AUDIO", "READ_MEDIA_VISUAL_USER_SELECTED", "READ_EXTERNAL_STORAGE", "WRITE_EXTERNAL_STORAGE", "MANAGE_EXTERNAL_STORAGE"], "имеет доступ к файлам"),
        new(["READ_MEDIA_VISUAL_USER_SELECTED"], "имеет доступ к выбранным фото или видео"),
        new(["READ_HEALTH", "READ_HEART", "READ_MEDICAL", "READ_SYMPTOM"], "может читать медицинские или фитнес-данные"),
        new(["BODY_SENSORS"], "может читать данные датчиков тела"),
        new(["BODY_SENSORS_BACKGROUND", "READ_HEALTH_DATA_IN_BACKGROUND"], "может читать health-данные в фоне"),
        new(["READ_HEALTH_DATA_HISTORY"], "может читать исторические health-данные"),
        new(["BIND_ACCESSIBILITY_SERVICE", "SYSTEM_ALERT_WINDOW", "BIND_NOTIFICATION_LISTENER_SERVICE"], "может читать данные с экрана, уведомления или показывать окна поверх других приложений"),
        new(["BIND_ACCESSIBILITY_SERVICE"], "может управлять интерфейсом"),
        new(["BIND_NOTIFICATION_LISTENER_SERVICE"], "может читать уведомления"),
        new(["SYSTEM_ALERT_WINDOW"], "может показывать окна поверх других приложений"),
        new(["BIND_VPN_SERVICE"], "может направлять трафик через VPN-сервис"),
        new(["PACKAGE_USAGE_STATS"], "может видеть, какие приложения используются"),
        new(["QUERY_ALL_PACKAGES"], "может видеть список установленных приложений"),
        new(["FOREGROUND_SERVICE_MEDIA_PROJECTION"], "может быть связано с записью экрана"),
        new(["READ_ASSIST_STRUCTURE_SCREEN_CONTENT"], "может получать содержимое экрана через assistant API"),
        new(["REQUEST_INSTALL_PACKAGES"], "может устанавливать APK из внешних источников"),
        new(["RECEIVE_BOOT_COMPLETED"], "может запускаться после перезагрузки устройства"),
        new(["REQUEST_IGNORE_BATTERY_OPTIMIZATIONS"], "может обходить ограничения энергосбережения"),
        new(["SCHEDULE_EXACT_ALARM", "USE_EXACT_ALARM"], "может точно будить приложение по расписанию"),
        new(["FOREGROUND_SERVICE"], "может длительно работать в фоне"),
        new(["POST_NOTIFICATIONS"], "может активно показывать уведомления"),
        new(["ACCESS_NETWORK_STATE"], "может отслеживать состояние сети"),
        new(["ACCESS_LOCAL_NETWORK"], "может обращаться к устройствам в локальной сети"),
        new(["NEARBY_WIFI_DEVICES"], "может видеть окружающие Wi-Fi сети и подключаться к ним"),
        new(["BLUETOOTH_CONNECT", "BLUETOOTH_SCAN"], "может использовать Bluetooth для поиска или обмена с устройствами рядом"),
        new(["NFC"], "может использовать NFC модуль"),
        new(["RANGING"], "может оценивать расстояние до nearby-устройств")
    ];

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
        if (permissions.Count == 0) return;

        var matchedRules = new bool[SpecificPermissionReasonRules.Length];
        for (var permissionIndex = 0; permissionIndex < permissions.Count; permissionIndex++)
        {
            var permission = permissions[permissionIndex];
            for (var ruleIndex = 0; ruleIndex < SpecificPermissionReasonRules.Length; ruleIndex++)
            {
                if (!matchedRules[ruleIndex] && SpecificPermissionReasonRules[ruleIndex].Matches(permission))
                    matchedRules[ruleIndex] = true;
            }
        }

        for (var ruleIndex = 0; ruleIndex < SpecificPermissionReasonRules.Length; ruleIndex++)
        {
            if (matchedRules[ruleIndex]) reasons.Add(SpecificPermissionReasonRules[ruleIndex].Reason);
        }
    }

    private readonly record struct PermissionReasonRule(string[] Tokens, string Reason)
    {
        public bool Matches(string permission)
        {
            for (var tokenIndex = 0; tokenIndex < Tokens.Length; tokenIndex++)
            {
                if (permission.Contains(Tokens[tokenIndex], StringComparison.Ordinal)) return true;
            }

            return false;
        }
    }
}
