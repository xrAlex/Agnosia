using Agnosia.Android.Permissions;
using Agnosia.Models;
using Agnosia.Unit.AndroidApi.Permissions;
using Agnosia.ViewModels;
using Xunit;
using static Agnosia.Unit.ViewModels.AppRiskReportBuilderTests;

namespace Agnosia.Unit.ViewModels;

public sealed class AppRiskReportCoverageTests
{
    [Fact]
    public void Every_rule_is_explained_alone_and_in_dense_real_engine_results()
    {
        var visited = new HashSet<string>();
        foreach (var sdk in new[] { 31, 33, 34, 36, 37 })
        foreach (var variant in new[] { "full", "offline", "limited" })
        {
            var analysis = AppPermissionRiskCatalog.Analyze(AppRiskFindingIsolationTests.BroadInput(sdk, variant));
            var report = AppRiskReportBuilder.Build(Snapshot(analysis.Findings.ToArray()) with
                { PermissionRiskLevel = analysis.Level }, true, false, false);
            Assert.Equal(analysis.Findings, report.SourceFindings);
            Assert.Equal(analysis.Level, report.Level);
            var body = Body(report);
            AssertClean(body);
            Assert.Equal(body, Body(Build(analysis.Findings.Reverse().ToArray())));
            foreach (var finding in analysis.Findings)
            {
                visited.Add(finding.RuleId);
                var alone = Body(Build(finding));
                Assert.NotEmpty(alone);
                AssertClean(alone);
                // Expected words describe independent effects, not internal template keys.
                Assert.True(ExpectedWords.TryGetValue(finding.RuleId, out var words), finding.RuleId);
                foreach (var word in words!)
                {
                    Assert.True(alone.Contains(word, StringComparison.OrdinalIgnoreCase), $"{finding.RuleId} alone: missing '{word}': {alone}");
                    Assert.True(body.Contains(word, StringComparison.OrdinalIgnoreCase), $"{finding.RuleId} grouped: missing '{word}'");
                }
            }
        }
        Assert.Equal(AppPermissionRiskCatalog.AllRuleIds.Order(), visited.Order());
        Assert.Equal(AppPermissionRiskCatalog.AllRuleIds.Order(), ExpectedWords.Keys.Order());
    }

    private static void AssertClean(string text)
    {
        foreach (var forbidden in new[] { "android.", "CR-", "SU-", "API", "FGS", "дополнительный риск", "во вред", "Оценка описывает", "не нужен", "отключить" })
            Assert.DoesNotContain(forbidden, text);
    }

    // Independent expectations include every distinct action/condition of every rule.
    internal static IReadOnlyDictionary<string, string[]> ExpectedWords { get; } = CreateExpectations();

    private static Dictionary<string, string[]> CreateExpectations()
    {
        var result = new Dictionary<string, string[]>();
        void Add(string ids, params string[] words)
        {
            foreach (var id in ids.Split(' ')) result.Add(id, words);
        }
        Add("CR-LOC-BG-01 CR-LOC-BG-02", "местоположение", "не открыто", "интернет");
        Add("SU-LOC-BG-01 SU-LOC-BG-02", "местоположение", "не открыто");
        Add("SU-LOC-01 SU-LOC-02", "местоположение");
        Add("SU-LOC-FGS-PERSIST-01 SU-LOC-FGS-PERSIST-02", "местоположение", "после включения", "переходите в другие приложения", "интернет");
        Add("SU-LOC-FGS-PERSIST-03 SU-LOC-FGS-PERSIST-04", "местоположение", "батареи", "переходите в другие приложения", "интернет");
        Add("CR-MIC-PERSIST-01", "микрофон", "после включения", "переходите в другие приложения", "интернет");
        Add("CR-MIC-PERSIST-02", "микрофон", "батареи", "переходите в другие приложения", "интернет");
        Add("CR-MIC-FGS-LEGACY-01 SU-MIC-FGS-14-01", "микрофон", "переходите в другие приложения", "интернет");
        Add("SU-MIC-PERSIST-01", "микрофон", "после включения");
        Add("SU-MIC-PERSIST-02", "микрофон", "батареи");
        Add("SU-MIC-01", "микрофон");
        Add("CR-CAM-PERSIST-01", "камер", "после включения", "переходите в другие приложения", "интернет");
        Add("CR-CAM-PERSIST-02", "камер", "батареи", "переходите в другие приложения", "интернет");
        Add("CR-CAM-FGS-LEGACY-01 SU-CAM-FGS-14-01", "камер", "переходите в другие приложения", "интернет");
        Add("SU-CAM-PERSIST-01", "камер", "после включения");
        Add("SU-CAM-PERSIST-02", "камер", "батареи");
        Add("SU-CAM-01", "камер");
        Add("CR-SMS-SEND-01", "новые", "отправлять", "расходам", "интернет");
        Add("CR-SMS-READ-01", "сохранённые", "интернет");
        Add("CR-SMS-RECEIVE-01", "новые", "интернет");
        Add("SU-SMS-READ-01", "сохранённые");
        Add("SU-SMS-RECEIVE-01", "новые");
        Add("SU-SMS-SEND-01", "отправлять", "расходам");
        Add("SU-SMS-MMS-01", "получать", "читать", "MMS");
        Add("SU-CALL-ANSWER-01", "отвечать", "звонки");
        Add("SU-CALL-HANDOVER-01", "продолжать звонки", "другом приложении");
        Add("SU-CALL-VOICEMAIL-ADD-01", "добавлять сообщения", "голосовую почту");
        Add("SU-CALL-STATE-01", "мобильной сети", "идёт ли звонок");
        Add("SU-GRAPH-ACCOUNTS-02", "аккаунтах");
        Add("SU-BLUETOOTH-SCAN-01", "обнаруживать", "Bluetooth");
        Add("SU-BLUETOOTH-CONNECT-01", "обмениваться", "Bluetooth");
        Add("SU-NEARBY-WIFI-01", "Wi-Fi", "взаимодействовать");
        Add("SU-APK-INSTALL-02", "предлагать установку");
        Add("SU-HEALTH-BG-01", "здоровье", "не открыто");
        Add("SU-HEALTH-SENSORS-BG-01", "датчиков здоровья", "не открыто");
        Add("SU-SMS-WAP-01", "служебные сообщения");
        Add("SU-SMS-ROLE-01", "основным для SMS");
        Add("CR-CALL-LOG-WRITE-01", "читать журнал", "изменять и удалять", "номер телефона", "интернет");
        Add("CR-CALL-LOG-01", "читать журнал", "интернет");
        Add("SU-CALL-LOG-READ-01", "читать журнал");
        Add("SU-CALL-LOG-WRITE-01", "изменять и удалять записи");
        Add("SU-CALL-ID-01", "номер телефона");
        Add("SU-CALL-PLACE-01", "совершать телефонные звонки", "расходам");
        Add("SU-CALL-MIC-01", "отвечать", "микрофон");
        Add("SU-CALL-STATE-PROF-01", "идёт ли звонок", "установлены");
        Add("SU-CALL-ROLE-01", "основным для телефонных звонков");
        Add("CR-HEALTH-BG-01", "здоровье", "не открыто", "интернет");
        Add("CR-HEALTH-SENSORS-BG-01", "датчиков здоровья", "не открыто", "интернет");
        Add("SU-HEALTH-READ-01", "читать", "здоровье");
        Add("SU-HEALTH-HISTORY-01", "историю записей");
        Add("SU-HEALTH-WRITE-01", "добавлять записи");
        Add("SU-HEALTH-SENSORS-01", "датчиков здоровья");
        Add("SU-HEALTH-ACTIVITY-01", "физическую активность");
        Add("CR-UI-ACC-OVERLAY-01", "действия в других приложениях", "поверх", "принять за окна", "интернет");
        Add("CR-UI-ACC-01", "содержимое экрана", "действия в других приложениях", "интернет");
        Add("CR-UI-NOTIF-OVERLAY-01", "читать уведомления других", "поверх", "принять за окна", "интернет");
        Add("CR-UI-ACC-NOTIF-01", "действия в других приложениях", "читать уведомления других", "интернет");
        Add("SU-UI-ACC-01", "содержимое экрана", "действия в других приложениях");
        Add("SU-UI-OVERLAY-01", "поверх других");
        Add("SU-NOTIF-01", "читать уведомления других");
        Add("SU-NOTIF-OVERLAY-01", "свои уведомления", "поверх других");
        Add("SU-UI-KEYBOARD-01", "вводите", "клавиатуру");
        Add("SU-UI-AUTOFILL-01", "заполнять автоматически");
        Add("SU-ASSIST-SCREEN-01", "помощник телефона");
        Add("CR-SCR-01", "получало изображение экрана", "передаваться", "общих файлах");
        Add("SU-SCR-FGS-01", "получало изображение экрана");
        Add("CR-PROF-USAGE-PERSIST-01", "когда вы пользуетесь", "после включения", "интернет");
        Add("CR-PROF-USAGE-PERSIST-02", "когда вы пользуетесь", "батареи", "интернет");
        Add("CR-PROF-INVENTORY-01", "когда вы пользуетесь", "установлены", "интернет");
        Add("SU-PROF-USAGE-01", "когда вы пользуетесь");
        Add("SU-PROF-INVENTORY-01", "установлены");
        Add("CR-FILE-ALL-PERSIST-01", "читать и изменять", "после включения", "интернет");
        Add("CR-FILE-ALL-PERSIST-02", "читать и изменять", "батареи", "интернет");
        Add("CR-FILE-ALL-MEDIA-LOC-01", "читать и изменять", "место съёмки", "интернет");
        Add("SU-FILE-ALL-01", "читать и изменять");
        Add("SU-MEDIA-LEGACY-01", "фотографии", "видео", "аудиофайлы", "общем хранилище");
        Add("SU-FILE-WRITE-LEGACY-01", "сохранять и изменять");
        Add("SU-MEDIA-IMG-01", "просматривать фотографии");
        Add("SU-MEDIA-VID-01", "просматривать видеозаписи");
        Add("SU-MEDIA-AUD-01", "читать аудиофайлы");
        Add("SU-MEDIA-PARTIAL-01", "выбранные вами фотографии и видео");
        Add("SU-MEDIA-LOC-LEGACY-01", "общем хранилище", "место съёмки");
        Add("SU-MEDIA-LOC-PARTIAL-01", "выбранные вами", "место съёмки");
        Add("SU-MEDIA-LOC-IMG-01", "просматривать фотографии", "место съёмки");
        Add("SU-MEDIA-LOC-VID-01", "просматривать видеозаписи", "место съёмки");
        Add("SU-GRAPH-CONTACTS-01", "читать контакты");
        Add("SU-GRAPH-WRITE-01", "изменять и удалять контакты");
        Add("SU-GRAPH-ACCOUNTS-01", "контакты", "аккаунтах", "номер телефона");
        Add("SU-CALENDAR-READ-01", "читать события календаря");
        Add("SU-CALENDAR-WRITE-01", "изменять и удалять события");
        Add("SU-ADS-ID-01", "рекламным сервисам");
        Add("SU-ADS-SERVICES-01", "подбора рекламы");
        Add("SU-NEARBY-BLUETOOTH-01", "Wi-Fi", "обнаруживать устройства Bluetooth");
        Add("SU-BLUETOOTH-EXFIL-01", "обнаруживать", "обмениваться данными");
        Add("SU-BLUETOOTH-ADVERTISE-01", "заметным");
        Add("SU-PROX-UWB-01", "точно измерять расстояние");
        Add("SU-PROX-RANGING-01", "измерять расстояние");
        Add("SU-LAN-16-01 SU-LAN-17-01", "той же сети");
        Add("SU-APK-INSTALL-01", "установку других приложений", "установлены");
        Add("SU-UI-ADMIN-01", "настройками телефона");
        Add("SU-VPN-01", "сетевым подключением");
        Add("SU-ASSIST-ROLE-01", "помощником телефона");
        return result;
    }
}
