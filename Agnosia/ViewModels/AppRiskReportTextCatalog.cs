namespace Agnosia.ViewModels;

// Explicit mappings keep changes to engine rules reviewable. Permission prefixes or score
// groups are not used to guess the meaning of a newly introduced rule.
internal static class AppRiskReportTextCatalog
{
    internal enum Topic { Location, Calls, Messages, Contacts, Calendar, Recording, Screen, Files, Health, Usage, Ads, Nearby, Device }

    internal sealed record Meaning(string Id, Topic Topic, string Action, string[] Signals,
        string Consequence = "", string? TransferObject = null, bool Prefix = false,
        bool DeclaredAvailable = false, string? ConfirmedText = null);

    internal sealed record Rule(string[] Meanings, bool Internet = false, bool ScreenChannels = false);

    internal static IReadOnlyDictionary<string, Meaning> Meanings { get; } = CreateMeanings();
    internal static IReadOnlyDictionary<string, Rule> Rules { get; } = CreateRules();
    internal static IEnumerable<string> RuleIds => Rules.Keys;

    private static Dictionary<string, Meaning> CreateMeanings()
    {
        var all = new Meaning[]
        {
            new("location-fine", Topic.Location, "знать точное местоположение", ["ACCESS_FINE_LOCATION"], TransferObject: "местоположение"),
            new("location-coarse", Topic.Location, "знать ваше приблизительное местоположение", ["ACCESS_COARSE_LOCATION"], TransferObject: "местоположение"),
            new("location-background", Topic.Location, "получать местоположение, даже когда приложение не открыто на экране", ["ACCESS_BACKGROUND_LOCATION"], "Это позволяет узнавать о ваших перемещениях."),
            new("call-read", Topic.Calls, "читать журнал звонков: с кем и когда вы связывались", ["READ_CALL_LOG"], TransferObject: "сведения о звонках"),
            new("call-write", Topic.Calls, "изменять и удалять записи о звонках", ["WRITE_CALL_LOG"]),
            new("phone-number", Topic.Calls, "узнавать ваш номер телефона", ["READ_PHONE_NUMBERS"], TransferObject: "номер телефона"),
            new("call-place", Topic.Calls, "совершать телефонные звонки", ["CALL_PHONE"], "Звонки могут приводить к расходам на связь."),
            new("call-answer", Topic.Calls, "отвечать на входящие звонки", ["ANSWER_PHONE_CALLS"]),
            new("call-state", Topic.Calls, "узнавать, подключён ли телефон к мобильной сети и идёт ли звонок", ["READ_PHONE_STATE"]),
            new("call-role", Topic.Calls, "обрабатывать телефонные звонки", ["android.observed.DefaultDialerRole"], ConfirmedText: "Приложение выбрано основным для телефонных звонков."),
            new("sms-read", Topic.Messages, "читать сохранённые SMS", ["READ_SMS"], TransferObject: "содержимое SMS"),
            new("sms-receive", Topic.Messages, "получать новые SMS", ["RECEIVE_SMS"], TransferObject: "содержимое новых SMS"),
            new("sms-send", Topic.Messages, "отправлять SMS", ["SEND_SMS"], "Отправка SMS может приводить к расходам на связь."),
            new("mms", Topic.Messages, "получать и читать SMS сообщения", ["RECEIVE_MMS"]),
            new("wap", Topic.Messages, "получать служебные сообщения от мобильного оператора", ["RECEIVE_WAP_PUSH"]),
            new("sms-role", Topic.Messages, "обрабатывать SMS", ["android.observed.DefaultSmsRole"], ConfirmedText: "Приложение выбрано основным для SMS."),
            new("contacts-read", Topic.Contacts, "читать контакты, включая имена и номера телефонов", ["READ_CONTACTS"]),
            new("contacts-write", Topic.Contacts, "изменять и удалять контакты", ["WRITE_CONTACTS"]),
            new("accounts", Topic.Contacts, "узнавать об аккаунтах на телефоне", ["GET_ACCOUNTS"]),
            new("calendar-read", Topic.Calendar, "читать события календаря", ["READ_CALENDAR"]),
            new("calendar-write", Topic.Calendar, "создавать, изменять и удалять события календаря", ["WRITE_CALENDAR"]),
            new("microphone", Topic.Recording, "использовать микрофон", ["RECORD_AUDIO"], TransferObject: "записи с микрофона"),
            new("camera", Topic.Recording, "использовать камеру", ["CAMERA"], TransferObject: "снимки и видео с камеры"),
            new("accessibility", Topic.Screen, "читать содержимое экрана и выполнять действия в других приложениях", ["BIND_ACCESSIBILITY_SERVICE"], TransferObject: "содержимое экрана"),
            new("notifications", Topic.Screen, "читать уведомления других приложений", ["BIND_NOTIFICATION_LISTENER_SERVICE"], TransferObject: "содержимое уведомлений"),
            new("overlay", Topic.Screen, "показывать свои окна поверх других приложений", ["SYSTEM_ALERT_WINDOW"]),
            new("own-notifications", Topic.Screen, "показывать свои уведомления", ["POST_NOTIFICATIONS"]),
            new("screen-capture", Topic.Screen, "получать изображение экрана", ["android.observed.MediaProjection"], TransferObject: "изображение экрана", ConfirmedText: "Во время проверки приложение получало изображение экрана."),
            new("assistant-screen", Topic.Screen, "читать доступное ему содержимое экрана как помощник телефона", ["READ_ASSIST_STRUCTURE_SCREEN_CONTENT", "android.observed.AssistantScreenContent"], TransferObject: "доступное помощнику содержимое экрана"),
            new("keyboard", Topic.Screen, "обрабатывать текст, который вы вводите через его клавиатуру", ["BIND_INPUT_METHOD"]),
            new("autofill", Topic.Screen, "читать данные в полях, которые оно помогает заполнять автоматически", ["BIND_AUTOFILL_SERVICE"]),
            new("files-all", Topic.Files, "читать и изменять файлы на телефоне", ["MANAGE_EXTERNAL_STORAGE"], TransferObject: "содержимое файлов"),
            new("files-legacy", Topic.Files, "читать файлы на телефоне", ["READ_EXTERNAL_STORAGE"]),
            new("files-write", Topic.Files, "сохранять и изменять файлы на телефоне", ["WRITE_EXTERNAL_STORAGE"]),
            new("photos", Topic.Files, "просматривать фотографии на телефоне", ["READ_MEDIA_IMAGES"]),
            new("videos", Topic.Files, "просматривать видеозаписи на телефоне", ["READ_MEDIA_VIDEO"]),
            new("audio", Topic.Files, "читать аудиофайлы на телефоне", ["READ_MEDIA_AUDIO"]),
            new("selected-media", Topic.Files, "читать только выбранные вами фотографии и видео", ["READ_MEDIA_VISUAL_USER_SELECTED"]),
            new("media-location", Topic.Files, "узнавать место съёмки из доступных ему фотографий и видео", ["ACCESS_MEDIA_LOCATION"], TransferObject: "места съёмки"),
            new("health-read", Topic.Health, "читать записи о здоровье", ["health.READ_"], TransferObject: "записи о здоровье", Prefix: true),
            new("health-write", Topic.Health, "добавлять записи о здоровье", ["health.WRITE_"], Prefix: true),
            new("health-history", Topic.Health, "читать историю записей о здоровье", ["health.READ_HEALTH_DATA_HISTORY"]),
            new("health-background", Topic.Health, "читать записи о здоровье, когда приложение не открыто на экране", ["health.READ_HEALTH_DATA_IN_BACKGROUND"]),
            new("body-sensors", Topic.Health, "получать показания датчиков здоровья", ["BODY_SENSORS"], TransferObject: "показания датчиков здоровья"),
            new("body-background", Topic.Health, "получать показания датчиков здоровья, когда приложение не открыто на экране", ["BODY_SENSORS_BACKGROUND"]),
            new("activity", Topic.Health, "определять вашу физическую активность", ["ACTIVITY_RECOGNITION"]),
            new("usage", Topic.Usage, "узнавать, какими приложениями и когда вы пользуетесь", ["PACKAGE_USAGE_STATS"], TransferObject: "сведения об использовании приложений"),
            new("inventory", Topic.Usage, "узнавать, какие приложения установлены на телефоне", ["QUERY_ALL_PACKAGES"], TransferObject: "список установленных приложений", DeclaredAvailable: true),
            new("ads-id", Topic.Ads, "использовать сведения, позволяющие рекламным сервисам узнавать вас в разных приложениях", ["com.google.android.gms.permission.AD_ID"]),
            new("ads-services", Topic.Ads, "использовать функции телефона для подбора рекламы или проверки того, сработала ли она", ["ACCESS_ADSERVICES_"], Prefix: true),
            new("nearby-wifi", Topic.Nearby, "находить устройства Wi-Fi поблизости и взаимодействовать с ними", ["NEARBY_WIFI_DEVICES"]),
            new("bluetooth-scan", Topic.Nearby, "обнаруживать устройства Bluetooth поблизости", ["BLUETOOTH_SCAN"]),
            new("bluetooth-connect", Topic.Nearby, "обмениваться данными с подключёнными устройствами Bluetooth", ["BLUETOOTH_CONNECT"]),
            new("bluetooth-advertise", Topic.Nearby, "делать телефон заметным для устройств Bluetooth поблизости", ["BLUETOOTH_ADVERTISE"]),
            new("ranging", Topic.Nearby, "измерять расстояние до устройств поблизости", ["RANGING"]),
            new("uwb", Topic.Nearby, "точно измерять расстояние до устройств поблизости", ["UWB_RANGING"]),
            new("lan-16", Topic.Nearby, "связываться с другими устройствами в той же сети", ["NEARBY_WIFI_DEVICES"]),
            new("lan-17", Topic.Nearby, "связываться с другими устройствами в той же сети", ["ACCESS_LOCAL_NETWORK"]),
            new("install", Topic.Device, "предлагать установку других приложений из файлов", ["REQUEST_INSTALL_PACKAGES"]),
            new("admin", Topic.Device, "управлять настройками телефона, например правилами блокировки экрана", ["BIND_DEVICE_ADMIN"]),
            new("vpn", Topic.Device, "управлять сетевым подключением телефона и настраивать VPN", ["BIND_VPN_SERVICE", "android.observed.VpnControl"]),
            new("assistant-role", Topic.Device, "работать системным помощником", ["android.observed.AssistantRole"], ConfirmedText: "Приложение выбрано помощником телефона.")
        };
        return all.ToDictionary(m => m.Id, StringComparer.Ordinal);
    }

    private static Dictionary<string, Rule> CreateRules()
    {
        var rules = new Dictionary<string, Rule>(StringComparer.Ordinal);
        void Add(string[] ids, string[] meanings, bool internet = false, bool screenChannels = false)
        {
            foreach (var id in ids) rules.Add(id, new(meanings, internet, screenChannels));
        }
        Add(["CR-LOC-BG-01"], ["location-fine", "location-background"], true);
        Add(["CR-LOC-BG-02"], ["location-coarse", "location-background"], true);
        Add(["SU-LOC-BG-01"], ["location-fine", "location-background"]);
        Add(["SU-LOC-BG-02"], ["location-coarse", "location-background"]);
        Add(["SU-LOC-01"], ["location-fine"]);
        Add(["SU-LOC-02"], ["location-coarse"]);
        Add(["SU-LOC-FGS-PERSIST-01", "SU-LOC-FGS-PERSIST-03"], ["location-fine"], true);
        Add(["SU-LOC-FGS-PERSIST-02", "SU-LOC-FGS-PERSIST-04"], ["location-coarse"], true);
        Add(["CR-MIC-PERSIST-01", "CR-MIC-PERSIST-02", "CR-MIC-FGS-LEGACY-01", "SU-MIC-FGS-14-01"], ["microphone"], true);
        Add(["SU-MIC-01", "SU-MIC-PERSIST-01", "SU-MIC-PERSIST-02"], ["microphone"]);
        Add(["CR-CAM-PERSIST-01", "CR-CAM-PERSIST-02", "CR-CAM-FGS-LEGACY-01", "SU-CAM-FGS-14-01"], ["camera"], true);
        Add(["SU-CAM-01", "SU-CAM-PERSIST-01", "SU-CAM-PERSIST-02"], ["camera"]);
        Add(["CR-CALL-LOG-01"], ["call-read"], true);
        Add(["CR-CALL-LOG-WRITE-01"], ["call-read", "call-write", "phone-number"], true);
        Add(["SU-CALL-LOG-READ-01"], ["call-read"]);
        Add(["SU-CALL-LOG-WRITE-01"], ["call-write"]);
        Add(["SU-CALL-ID-01"], ["phone-number"]);
        Add(["SU-CALL-PLACE-01"], ["call-place"]);
        Add(["SU-CALL-MIC-01"], ["call-answer", "microphone"]);
        Add(["SU-CALL-STATE-PROF-01"], ["call-state", "inventory"]);
        Add(["SU-CALL-ROLE-01"], ["call-role"]);
        Add(["CR-SMS-SEND-01"], ["sms-receive", "sms-send"], true);
        Add(["CR-SMS-READ-01"], ["sms-read"], true);
        Add(["CR-SMS-RECEIVE-01"], ["sms-receive"], true);
        Add(["SU-SMS-READ-01"], ["sms-read"]);
        Add(["SU-SMS-RECEIVE-01"], ["sms-receive"]);
        Add(["SU-SMS-SEND-01"], ["sms-send"]);
        Add(["SU-SMS-MMS-01"], ["mms"]);
        Add(["SU-SMS-WAP-01"], ["wap"]);
        Add(["SU-SMS-ROLE-01"], ["sms-role"]);
        Add(["SU-GRAPH-CONTACTS-01"], ["contacts-read"]);
        Add(["SU-GRAPH-WRITE-01"], ["contacts-write"]);
        Add(["SU-GRAPH-ACCOUNTS-01"], ["contacts-read", "accounts", "phone-number"]);
        Add(["SU-CALENDAR-READ-01"], ["calendar-read"]);
        Add(["SU-CALENDAR-WRITE-01"], ["calendar-write"]);
        Add(["CR-HEALTH-BG-01"], ["health-read", "health-background"], true);
        Add(["CR-HEALTH-SENSORS-BG-01"], ["body-sensors", "body-background"], true);
        Add(["SU-HEALTH-READ-01"], ["health-read"]);
        Add(["SU-HEALTH-WRITE-01"], ["health-write"]);
        Add(["SU-HEALTH-HISTORY-01"], ["health-read", "health-history"]);
        Add(["SU-HEALTH-SENSORS-01"], ["body-sensors"]);
        Add(["SU-HEALTH-ACTIVITY-01"], ["activity"]);
        Add(["CR-UI-ACC-01"], ["accessibility"], true);
        Add(["CR-UI-ACC-OVERLAY-01"], ["accessibility", "overlay"], true);
        Add(["CR-UI-NOTIF-OVERLAY-01"], ["notifications", "overlay"], true);
        Add(["CR-UI-ACC-NOTIF-01"], ["accessibility", "notifications"], true);
        Add(["SU-UI-ACC-01"], ["accessibility"]);
        Add(["SU-UI-OVERLAY-01"], ["overlay"]);
        Add(["SU-NOTIF-01"], ["notifications"]);
        Add(["SU-NOTIF-OVERLAY-01"], ["own-notifications", "overlay"]);
        Add(["SU-UI-KEYBOARD-01"], ["keyboard"]);
        Add(["SU-UI-AUTOFILL-01"], ["autofill"]);
        Add(["SU-ASSIST-SCREEN-01"], ["assistant-screen"]);
        Add(["CR-SCR-01"], ["screen-capture"], screenChannels: true);
        Add(["SU-SCR-FGS-01"], ["screen-capture"]);
        Add(["CR-PROF-USAGE-PERSIST-01", "CR-PROF-USAGE-PERSIST-02"], ["usage"], true);
        Add(["CR-PROF-INVENTORY-01"], ["usage", "inventory"], true);
        Add(["SU-PROF-USAGE-01"], ["usage"]);
        Add(["SU-PROF-INVENTORY-01"], ["inventory"]);
        Add(["CR-FILE-ALL-PERSIST-01", "CR-FILE-ALL-PERSIST-02"], ["files-all"], true);
        Add(["CR-FILE-ALL-MEDIA-LOC-01"], ["files-all", "media-location"], true);
        Add(["SU-FILE-ALL-01"], ["files-all"]);
        Add(["SU-MEDIA-LEGACY-01"], ["files-legacy"]);
        Add(["SU-FILE-WRITE-LEGACY-01"], ["files-write"]);
        Add(["SU-MEDIA-IMG-01"], ["photos"]);
        Add(["SU-MEDIA-VID-01"], ["videos"]);
        Add(["SU-MEDIA-AUD-01"], ["audio"]);
        Add(["SU-MEDIA-PARTIAL-01"], ["selected-media"]);
        Add(["SU-MEDIA-LOC-LEGACY-01"], ["files-legacy", "media-location"]);
        Add(["SU-MEDIA-LOC-IMG-01"], ["photos", "media-location"]);
        Add(["SU-MEDIA-LOC-VID-01"], ["videos", "media-location"]);
        Add(["SU-ADS-ID-01"], ["ads-id"]);
        Add(["SU-ADS-SERVICES-01"], ["ads-services"]);
        Add(["SU-NEARBY-BLUETOOTH-01"], ["nearby-wifi", "bluetooth-scan"]);
        Add(["SU-BLUETOOTH-EXFIL-01"], ["bluetooth-scan", "bluetooth-connect"]);
        Add(["SU-BLUETOOTH-ADVERTISE-01"], ["bluetooth-advertise"]);
        Add(["SU-PROX-UWB-01"], ["uwb"]);
        Add(["SU-PROX-RANGING-01"], ["ranging"]);
        Add(["SU-LAN-16-01"], ["lan-16"]);
        Add(["SU-LAN-17-01"], ["lan-17"]);
        Add(["SU-APK-INSTALL-01"], ["install", "inventory"]);
        Add(["SU-UI-ADMIN-01"], ["admin"]);
        Add(["SU-VPN-01"], ["vpn"]);
        Add(["SU-ASSIST-ROLE-01"], ["assistant-role"]);
        return rules;
    }
}
