using Agnosia.Android.Permissions;
using Agnosia.Models;
using Agnosia.Unit.TestSupport;
using Agnosia.ViewModels;
using Xunit;

namespace Agnosia.Unit.AndroidApi.Permissions;

public sealed class AppRiskFindingIsolationTests
{
    private static string Permission(string name) => "android.permission." + name;

    private static readonly string[] Channels =
    [
        "INTERNET", "ACCESS_LOCAL_NETWORK", "BLUETOOTH_CONNECT", "NFC", "SEND_SMS",
        "WRITE_EXTERNAL_STORAGE", "MANAGE_EXTERNAL_STORAGE"
    ];

    internal static AppPermissionRiskInput BroadInput(int sdk, string variant)
    {
        var permissions = Permissions.Concat(Channels).Select(Permission)
            .Append("com.google.android.gms.permission.AD_ID")
            .Where(p => variant != "offline" || p != Permission("INTERNET"))
            .Where(p => variant != "limited" || !new[]
            {
                Permission("ACCESS_BACKGROUND_LOCATION"), Permission("READ_MEDIA_IMAGES"), Permission("READ_MEDIA_VIDEO")
            }.Contains(p)).ToArray();
        return new AppPermissionRiskInput(permissions, DeviceSdkVersion: sdk,
            TargetSdkVersion: sdk == 31 ? 29 : sdk, GrantedPermissions: permissions,
            ForegroundServiceTypes: ["camera", "microphone", "location", "mediaProjection"],
            ObservedSignals: ["android.observed.MediaProjection", "android.observed.VpnControl",
                "android.observed.AssistantScreenContent", "android.observed.DefaultSmsRole",
                "android.observed.DefaultDialerRole", "android.observed.AssistantRole"],
            IsAccessibilityServiceEnabled: true, IsNotificationListenerEnabled: true, CanDrawOverlays: true,
            HasUsageStatsAccess: true, IsVpnControlEnabled: true, IsAssistantScreenContentEnabled: true,
            IsMediaProjectionActive: true, HasManageExternalStorageAccess: true, CanRequestPackageInstalls: true,
            IsIgnoringBatteryOptimizations: true, IsLocalNetworkRestrictionEnabled: true,
            IsInputMethodEnabled: true, IsAutofillServiceEnabled: true, IsDeviceAdminEnabled: true);
    }

    // Deliberately broad application fixtures expose cross-contamination between findings.
    private static readonly string[] Permissions =
    [
        "ACCESS_BACKGROUND_LOCATION", "ACCESS_COARSE_LOCATION", "ACCESS_FINE_LOCATION", "ACCESS_MEDIA_LOCATION",
        "ANSWER_PHONE_CALLS", "BIND_ACCESSIBILITY_SERVICE", "BIND_NOTIFICATION_LISTENER_SERVICE", "BIND_VPN_SERVICE",
        "BIND_INPUT_METHOD", "BIND_AUTOFILL_SERVICE", "BIND_DEVICE_ADMIN", "BLUETOOTH_SCAN", "RECEIVE_BOOT_COMPLETED",
        "CAMERA", "FOREGROUND_SERVICE", "FOREGROUND_SERVICE_CAMERA", "FOREGROUND_SERVICE_LOCATION",
        "FOREGROUND_SERVICE_MEDIA_PROJECTION", "FOREGROUND_SERVICE_MICROPHONE", "GET_ACCOUNTS",
        "REQUEST_IGNORE_BATTERY_OPTIMIZATIONS", "NEARBY_WIFI_DEVICES", "PACKAGE_USAGE_STATS", "POST_NOTIFICATIONS",
        "QUERY_ALL_PACKAGES", "READ_ASSIST_STRUCTURE_SCREEN_CONTENT", "READ_CALL_LOG", "READ_CONTACTS",
        "READ_EXTERNAL_STORAGE", "READ_MEDIA_AUDIO", "READ_MEDIA_IMAGES", "READ_MEDIA_VISUAL_USER_SELECTED",
        "READ_MEDIA_VIDEO", "READ_PHONE_NUMBERS", "READ_SMS", "RECEIVE_SMS", "RECORD_AUDIO",
        "REQUEST_INSTALL_PACKAGES", "SYSTEM_ALERT_WINDOW", "WRITE_CALL_LOG", "READ_PHONE_STATE", "RANGING",
        "READ_CALENDAR", "WRITE_CALENDAR", "WRITE_CONTACTS", "CALL_PHONE", "RECEIVE_MMS", "RECEIVE_WAP_PUSH",
        "BODY_SENSORS", "BODY_SENSORS_BACKGROUND", "ACTIVITY_RECOGNITION", "UWB_RANGING", "BLUETOOTH_ADVERTISE",
        "health.READ_HEART_RATE", "health.WRITE_STEPS", "health.READ_HEALTH_DATA_IN_BACKGROUND",
        "health.READ_HEALTH_DATA_HISTORY", "ACCESS_ADSERVICES_TOPICS"
    ];

    [Fact]
    public void Every_catalog_rule_reports_only_channels_belonging_to_its_combination()
    {
        var visited = new HashSet<string>();
        var errors = new List<string>();
        foreach (var sdk in new[] { 31, 33, 34, 36, 37 })
        foreach (var variant in new[] { "full", "offline", "limited" })
        {
            var input = BroadInput(sdk, variant);
            var analysis = AppPermissionRiskCatalog.Analyze(input);
            var report = AppRiskReportBuilder.Build(TestSnapshots.App(ProfileKind.Personal) with
            {
                PermissionRiskFindings = analysis.Findings, PermissionRiskLevel = analysis.Level
            }, true, false, false);
            Assert.Equal(analysis.Findings.OrderBy(f => f.RuleId),
                report.SourceFindings.OrderBy(f => f.RuleId));
            foreach (var finding in analysis.Findings)
            {
                visited.Add(finding.RuleId);
                var expected = ExpectedChannels(finding.RuleId, sdk, variant == "offline").Select(Permission).Order().ToArray();
                var actual = finding.Evidence.Select(e => e.SignalId).Intersect(Channels.Select(Permission)).Order().ToArray();
                if (!expected.SequenceEqual(actual))
                    errors.Add($"{finding.RuleId}, SDK {sdk}, {variant}: expected [{string.Join(", ", expected)}], actual [{string.Join(", ", actual)}]");
            }
        }

        Assert.Equal(AppPermissionRiskCatalog.AllRuleIds.Order(), visited.Order());
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Take(12)));
    }

    private static IEnumerable<string> ExpectedChannels(string ruleId, int sdk, bool offline)
    {
        if (ruleId == "CR-SCR-01")
        {
            return Channels.Where(c => (c != "INTERNET" || !offline)
                && (c != "ACCESS_LOCAL_NETWORK" || sdk >= 37)
                && (c != "WRITE_EXTERNAL_STORAGE" || sdk == 31));
        }

        var result = new List<string>();
        if (ruleId.StartsWith("CR-", StringComparison.Ordinal)
            || ruleId.StartsWith("SU-LOC-FGS-PERSIST-", StringComparison.Ordinal)
            || ruleId is "SU-MIC-FGS-14-01" or "SU-CAM-FGS-14-01") result.Add("INTERNET");
        if (ruleId is "CR-SMS-SEND-01" or "SU-SMS-SEND-01") result.Add("SEND_SMS");
        if (ruleId.StartsWith("CR-FILE-ALL-", StringComparison.Ordinal) || ruleId == "SU-FILE-ALL-01")
            result.Add("MANAGE_EXTERNAL_STORAGE");
        if (ruleId == "SU-FILE-WRITE-LEGACY-01") result.Add("WRITE_EXTERNAL_STORAGE");
        if (ruleId == "SU-BLUETOOTH-EXFIL-01") result.Add("BLUETOOTH_CONNECT");
        if (ruleId == "SU-LAN-17-01") result.Add("ACCESS_LOCAL_NETWORK");
        return result;
    }

    [Theory]
    [InlineData("SU-ADS-ID-01", "com.google.android.gms.permission.AD_ID")]
    [InlineData("SU-ADS-SERVICES-01", "android.permission.ACCESS_ADSERVICES_TOPICS")]
    [InlineData("SU-CALENDAR-WRITE-01", "android.permission.WRITE_CALENDAR")]
    [InlineData("SU-GRAPH-CONTACTS-01", "android.permission.READ_CONTACTS")]
    [InlineData("SU-MEDIA-IMG-01", "android.permission.READ_MEDIA_IMAGES")]
    [InlineData("SU-HEALTH-WRITE-01", "android.permission.health.WRITE_STEPS")]
    [InlineData("SU-BLUETOOTH-ADVERTISE-01", "android.permission.BLUETOOTH_ADVERTISE")]
    public void Unrelated_channels_do_not_change_finding_or_report_text(string ruleId, string permission)
    {
        var input = new AppPermissionRiskInput([permission], DeviceSdkVersion: 37, TargetSdkVersion: 37,
            GrantedPermissions: [permission]);
        var baseline = AppPermissionRiskCatalog.Analyze(input).Findings.Single(f => f.RuleId == ruleId);
        var expandedPermissions = Channels.Select(Permission).Append(permission).ToArray();
        var expanded = AppPermissionRiskCatalog.Analyze(input with
        {
            RequestedPermissions = expandedPermissions, GrantedPermissions = expandedPermissions,
            HasManageExternalStorageAccess = true
        }).Findings.Single(f => f.RuleId == ruleId);

        Assert.Equal(baseline.Evidence, expanded.Evidence);
        Assert.Equal(baseline.RuleLevel, expanded.RuleLevel);
        var snapshot = TestSnapshots.App(ProfileKind.Personal);
        var originalReport = AppRiskReportBuilder.Build(snapshot with { PermissionRiskFindings = [baseline] }, true, false, false);
        var expandedReport = AppRiskReportBuilder.Build(snapshot with { PermissionRiskFindings = [expanded] }, true, false, false);
        Assert.Equal(originalReport.Sections.Select(s => s.Title), expandedReport.Sections.Select(s => s.Title));
        Assert.Equal(originalReport.Sections.SelectMany(s => s.Paragraphs), expandedReport.Sections.SelectMany(s => s.Paragraphs));
    }

    [Theory]
    [InlineData("NFC")]
    [InlineData("BLUETOOTH_CONNECT")]
    [InlineData("INTERNET")]
    public void Screen_capture_combination_keeps_its_effective_transmission_channel(string channel)
    {
        var permissions = new[] { Permission("FOREGROUND_SERVICE_MEDIA_PROJECTION"), Permission(channel) };
        var input = new AppPermissionRiskInput(permissions, DeviceSdkVersion: 34, GrantedPermissions: permissions,
            ForegroundServiceTypes: ["mediaProjection"], IsMediaProjectionActive: true);
        var finding = AppPermissionRiskCatalog.Analyze(input).Findings.Single(f => f.RuleId == "CR-SCR-01");
        Assert.Contains(finding.Evidence, e => e.SignalId == Permission(channel));
        Assert.DoesNotContain(AppPermissionRiskCatalog.Analyze(input with
        {
            GrantedPermissions = [Permission("FOREGROUND_SERVICE_MEDIA_PROJECTION")],
            DeniedPermissions = [Permission(channel)]
        }).Findings, f => f.RuleId == "CR-SCR-01");
    }

    [Fact]
    public void Advertising_section_contains_only_advertising_capabilities()
    {
        var permissions = Channels.Select(Permission).Append("com.google.android.gms.permission.AD_ID")
            .Append(Permission("ACCESS_ADSERVICES_TOPICS")).ToArray();
        var result = AppPermissionRiskCatalog.Analyze(new AppPermissionRiskInput(permissions,
            DeviceSdkVersion: 37, GrantedPermissions: permissions));
        var findings = result.Findings.Where(f => f.RuleId.StartsWith("SU-ADS-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, findings.Length);
        var report = AppRiskReportBuilder.Build(TestSnapshots.App(ProfileKind.Personal) with
            { PermissionRiskFindings = result.Findings, PermissionRiskLevel = result.Level }, true, false, false);
        var section = Assert.Single(report.Sections, s => s.Title == "Реклама");
        var text = string.Join(" ", section.Paragraphs);
        Assert.DoesNotContain("NFC", text);
        Assert.DoesNotContain("Bluetooth", text);
        Assert.DoesNotContain("интернет", text);
    }
}
