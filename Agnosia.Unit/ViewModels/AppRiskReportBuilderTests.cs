using Agnosia.Android.Permissions;
using Agnosia.Models;
using Agnosia.Unit.TestSupport;
using Agnosia.ViewModels;
using Xunit;

namespace Agnosia.Unit.ViewModels;

public sealed class AppRiskReportBuilderTests
{
    internal static AppPermissionRiskFinding Finding(string id, params string[] signals) =>
        new(id, id.StartsWith("CR-") ? AppPermissionRiskLevel.Critical : AppPermissionRiskLevel.Dangerous,
            signals.Select(s => new AppPermissionRiskEvidence(
                s.StartsWith("android.") ? s : "android.permission." + s, AppPermissionRiskEvidenceState.Granted)).ToArray());

    internal static AppSnapshot Snapshot(params AppPermissionRiskFinding[] findings) =>
        TestSnapshots.App(ProfileKind.Personal) with
        {
            PermissionRiskFindings = findings,
            PermissionRiskLevel = findings.Length == 0 ? AppPermissionRiskLevel.Safe : findings.Max(f => f.RuleLevel),
            PermissionRiskEvaluatedAtUtc = new DateTimeOffset(2026, 9, 28, 11, 32, 0, TimeSpan.Zero)
        };

    internal static AppRiskReportViewModel Build(params AppPermissionRiskFinding[] findings) =>
        AppRiskReportBuilder.Build(Snapshot(findings), true, false, false, TimeZoneInfo.Utc);

    internal static string Body(AppRiskReportViewModel report) =>
        string.Join("\n", report.Sections.SelectMany(s => s.Paragraphs));

    internal static string VisibleText(AppRiskReportViewModel report) =>
        string.Join("\n", new[] { report.LevelText, report.CheckedAtText, report.Summary, Body(report), report.StatusText, report.ProtectionText });

    [Fact]
    public void Call_log_overlap_is_described_once_without_losing_write_or_phone_number()
    {
        var report = Build(
            Finding("SU-CALL-LOG-READ-01", "READ_CALL_LOG"),
            Finding("CR-CALL-LOG-01", "READ_CALL_LOG", "INTERNET"),
            Finding("CR-CALL-LOG-WRITE-01", "READ_CALL_LOG", "WRITE_CALL_LOG", "READ_PHONE_NUMBERS", "INTERNET"),
            Finding("SU-CALL-LOG-WRITE-01", "WRITE_CALL_LOG"), Finding("SU-CALL-ID-01", "READ_PHONE_NUMBERS"));
        Assert.Single(report.Sections);
        var body = Body(report);
        Assert.Equal(1, body.Split("читать журнал звонков").Length - 1);
        Assert.Contains("изменять и удалять", body);
        Assert.Contains("номер телефона", body);
        Assert.Equal(1, body.Split("через интернет").Length - 1);
        Assert.Equal(5, report.SourceFindings.Count);
    }

    [Fact]
    public void File_persistence_preserves_both_conditions_once()
    {
        var report = Build(Finding("CR-FILE-ALL-PERSIST-01", "MANAGE_EXTERNAL_STORAGE", "RECEIVE_BOOT_COMPLETED", "INTERNET"),
            Finding("CR-FILE-ALL-PERSIST-02", "MANAGE_EXTERNAL_STORAGE", "REQUEST_IGNORE_BATTERY_OPTIMIZATIONS", "INTERNET"),
            Finding("SU-FILE-ALL-01", "MANAGE_EXTERNAL_STORAGE"));
        var body = Body(report);
        Assert.Equal(1, body.Split("файлы на телефоне").Length - 1);
        Assert.Contains("после включения телефона", body);
        Assert.Contains("батареи", body);
        Assert.DoesNotContain("постоянно", body);
    }

    [Fact]
    public void Sms_sending_keeps_costs_and_does_not_hide_reading_saved_messages()
    {
        var body = Body(Build(Finding("CR-SMS-SEND-01", "RECEIVE_SMS", "SEND_SMS", "INTERNET"),
            Finding("SU-SMS-RECEIVE-01", "RECEIVE_SMS"), Finding("SU-SMS-SEND-01", "SEND_SMS"),
            Finding("CR-SMS-READ-01", "READ_SMS", "INTERNET")));
        Assert.Contains("сохранённые", body);
        Assert.Contains("новые", body);
        Assert.Contains("отправлять", body);
        Assert.Equal(1, body.Split("расходам").Length - 1);
    }

    [Fact]
    public void Health_history_background_and_sensors_are_not_lost()
    {
        var report = Build(Finding("SU-HEALTH-READ-01", "health.READ_HEART_RATE"),
            Finding("SU-HEALTH-HISTORY-01", "health.READ_HEART_RATE", "health.READ_HEALTH_DATA_HISTORY"),
            Finding("CR-HEALTH-BG-01", "health.READ_HEART_RATE", "health.READ_HEALTH_DATA_IN_BACKGROUND", "INTERNET"),
            Finding("SU-HEALTH-SENSORS-01", "BODY_SENSORS"));
        Assert.Single(report.Sections);
        var text = Body(report);
        Assert.Contains("историю записей", text);
        Assert.Contains("не открыто на экране", text);
        Assert.Contains("датчиков здоровья", text);
    }

    [Fact]
    public void Only_own_notifications_do_not_become_reading_other_notifications()
    {
        var body = Body(Build(Finding("SU-NOTIF-OVERLAY-01", "POST_NOTIFICATIONS", "SYSTEM_ALERT_WINDOW")));
        Assert.Contains("свои уведомления", body);
        Assert.Contains("поверх других приложений", body);
        Assert.DoesNotContain("читать уведомления", body);
    }

    [Fact]
    public void Internet_and_nearby_channels_do_not_spread_between_topics()
    {
        var report = Build(Finding("CR-CALL-LOG-01", "READ_CALL_LOG", "INTERNET"),
            new("SU-ADS-ID-01", AppPermissionRiskLevel.Safe, [new("com.google.android.gms.permission.AD_ID", AppPermissionRiskEvidenceState.Declared)]),
            Finding("SU-BLUETOOTH-EXFIL-01", "BLUETOOTH_CONNECT", "BLUETOOTH_SCAN"),
            Finding("SU-GRAPH-CONTACTS-01", "READ_CONTACTS"));
        var ads = Assert.Single(report.Sections, s => s.Title == "Реклама");
        Assert.DoesNotContain("интернет", string.Join(" ", ads.Paragraphs));
        Assert.DoesNotContain("Bluetooth", string.Join(" ", ads.Paragraphs));
        var contacts = Assert.Single(report.Sections, s => s.Title.Contains("Контакты"));
        Assert.DoesNotContain("интернет", string.Join(" ", contacts.Paragraphs));
    }

    [Theory]
    [InlineData(AppPermissionRiskEvidenceState.Declared)]
    [InlineData(AppPermissionRiskEvidenceState.Unknown)]
    [InlineData(AppPermissionRiskEvidenceState.Denied)]
    public void Unconfirmed_access_never_becomes_granted_in_text(AppPermissionRiskEvidenceState state)
    {
        var report = Build(new AppPermissionRiskFinding("SU-CALL-LOG-READ-01", AppPermissionRiskLevel.Dangerous,
            [new("android.permission.READ_CALL_LOG", state)]));
        Assert.DoesNotContain("может читать", VisibleText(report));
        Assert.Contains("запрашивает", VisibleText(report));
    }

    [Fact]
    public void Conflicting_states_are_conservative_and_input_order_does_not_matter()
    {
        var findings = new[] { Finding("CR-CALL-LOG-01", "READ_CALL_LOG", "INTERNET"),
            new AppPermissionRiskFinding("SU-CALL-LOG-READ-01", AppPermissionRiskLevel.Dangerous,
                [new("android.permission.READ_CALL_LOG", AppPermissionRiskEvidenceState.Denied)]) };
        var report = Build(findings);
        Assert.DoesNotContain("может читать", VisibleText(report));
        Assert.DoesNotContain("могут передаваться", VisibleText(report));
        Assert.Equal(VisibleText(report), VisibleText(Build(findings.Reverse().ToArray())));
    }

    [Fact]
    public void Foreground_access_and_declared_service_do_not_promise_background_recording()
    {
        var finding = Finding("SU-MIC-FGS-14-01", "RECORD_AUDIO", "FOREGROUND_SERVICE_MICROPHONE", "INTERNET");
        finding = finding with { Evidence = [new("android.permission.RECORD_AUDIO", AppPermissionRiskEvidenceState.ForegroundOnly),
            new("android.foregroundServiceType.microphone", AppPermissionRiskEvidenceState.Declared),
            new("android.permission.INTERNET", AppPermissionRiskEvidenceState.Granted)] };
        var text = VisibleText(Build(finding));
        Assert.Contains("во время использования", text);
        Assert.DoesNotContain("даже когда", text);
        Assert.DoesNotContain("постоянно", text);
    }

    [Fact]
    public void Unknown_rule_stays_visible_and_user_rejected_copy_is_absent()
    {
        var report = Build(Finding("FUTURE-01", "NEW_PERMISSION"));
        var text = VisibleText(report);
        Assert.Contains("дополнительный риск", text);
        foreach (var forbidden in new[] { "FUTURE-01", "NEW_PERMISSION", "android.", "настройках", "отключить", "во вред", "Оценка описывает", "Технические" })
            Assert.DoesNotContain(forbidden, text);
        Assert.Single(report.SourceFindings);
    }

    [Fact]
    public void Every_engine_rule_has_an_explicit_text_definition()
    {
        Assert.Equal(AppPermissionRiskCatalog.AllRuleIds.Order(), AppRiskReportTextCatalog.RuleIds.Order());
    }

    [Fact]
    public void Background_condition_does_not_grant_the_missing_underlying_access()
    {
        var finding = Finding("CR-LOC-BG-01", "ACCESS_BACKGROUND_LOCATION", "INTERNET") with
        {
            Evidence = [new("android.permission.ACCESS_FINE_LOCATION", AppPermissionRiskEvidenceState.Denied),
                new("android.permission.ACCESS_BACKGROUND_LOCATION", AppPermissionRiskEvidenceState.Granted),
                new("android.permission.INTERNET", AppPermissionRiskEvidenceState.Granted)]
        };
        var text = VisibleText(Build(finding));
        Assert.DoesNotContain("может получать местоположение", text);
        Assert.DoesNotContain("позволяет узнавать о ваших перемещениях", text);
        Assert.DoesNotContain("передавать через интернет", text);
    }

    [Fact]
    public void Persistence_condition_does_not_introduce_a_camera_for_microphone_only_app()
    {
        var report = Build(Finding("SU-MIC-PERSIST-01", "RECORD_AUDIO", "RECEIVE_BOOT_COMPLETED"));
        Assert.DoesNotContain("камер", Body(report));
        Assert.Contains("после включения телефона", Body(report));
    }

    [Fact]
    public void File_only_report_has_a_file_heading_and_no_photography_claim()
    {
        Assert.Equal("Файлы", Build(Finding("SU-FILE-ALL-01", "MANAGE_EXTERNAL_STORAGE")).Sections[0].Title);
        var photos = Body(Build(Finding("SU-MEDIA-LOC-IMG-01", "READ_MEDIA_IMAGES", "ACCESS_MEDIA_LOCATION")));
        Assert.DoesNotContain("видео", photos);
    }

    [Fact]
    public void Health_background_evidence_is_explained_even_without_internet_combination()
    {
        var text = Body(Build(Finding("SU-HEALTH-READ-01", "health.READ_HEART_RATE", "health.READ_HEALTH_DATA_IN_BACKGROUND")));
        Assert.Contains("не открыто на экране", text);
        Assert.DoesNotContain("интернет", text);
    }

    [Fact]
    public void Usage_service_condition_is_preserved()
    {
        var text = Body(Build(Finding("CR-PROF-USAGE-PERSIST-01", "PACKAGE_USAGE_STATS", "RECEIVE_BOOT_COMPLETED", "FOREGROUND_SERVICE", "INTERNET")));
        Assert.Contains("продолжать работу", text);
    }

    [Fact]
    public void Photos_and_audio_heading_does_not_introduce_video_access()
    {
        var report = Build(Finding("SU-MEDIA-IMG-01", "READ_MEDIA_IMAGES"), Finding("SU-MEDIA-AUD-01", "READ_MEDIA_AUDIO"));
        Assert.Equal("Фотографии и аудио", Assert.Single(report.Sections).Title);
    }

    [Fact]
    public void Broad_file_access_does_not_claim_media_is_limited_to_selected_items()
    {
        var input = Agnosia.Unit.AndroidApi.Permissions.AppRiskFindingIsolationTests.BroadInput(34, "limited");
        var findings = AppPermissionRiskCatalog.Analyze(input).Findings;
        Assert.Contains(findings, f => f.RuleId == "SU-MEDIA-PARTIAL-01");
        Assert.Contains(findings, f => f.RuleId == "SU-FILE-ALL-01");
        var report = Build(findings.ToArray());
        Assert.Contains("файлы на телефоне", Body(report));
        Assert.DoesNotContain("только выбранные", Body(report));
        Assert.Contains("выбранные вами фотографии и видео", Body(report));

        var selected = Build(Finding("SU-MEDIA-PARTIAL-01", "READ_MEDIA_VISUAL_USER_SELECTED"));
        Assert.Contains("только выбранные", Body(selected));
    }
}
