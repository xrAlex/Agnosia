using Agnosia.Models;
using Agnosia.Unit.TestDoubles;
using Agnosia.Unit.TestSupport;
using Agnosia.ViewModels;
using Xunit;
using static Agnosia.Unit.ViewModels.AppRiskReportBuilderTests;

namespace Agnosia.Unit.ViewModels;

public sealed class AppRiskReportStateTests
{
    [Fact]
    public void App_item_invalidates_report_for_snapshot_refresh_status_and_protection()
    {
        var snapshot = Snapshot(Finding("SU-MIC-01", "RECORD_AUDIO"));
        var app = TestWorkspaceFactory.CreateApp(TestWorkspaceFactory.Create(), snapshot);
        var initial = app.RiskReport;
        var changes = new List<string?>();
        app.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        app.IsRiskRefreshing = true;
        Assert.Contains(nameof(app.RiskReport), changes);
        Assert.Equal("Обновляем оценку…", app.RiskReport.StatusText);
        changes.Clear();
        app.IsRiskRefreshing = false;
        app.RiskRefreshFailed = true;
        Assert.Contains("предыдущей проверки", app.RiskReport.StatusText);
        app.ApplySnapshot(snapshot with { IsInternetBlocked = true });
        Assert.NotEmpty(app.RiskReport.ProtectionText);
        app.ApplySnapshot(snapshot with { PermissionRiskFindings = [], PermissionRiskLevel = AppPermissionRiskLevel.Safe });
        Assert.Empty(app.RiskReport.Sections);
        Assert.NotSame(initial, app.RiskReport);
    }

    [Fact]
    public async Task Disabled_owner_module_never_exposes_cached_report()
    {
        var services = new TestPlatformServices
        {
            DashboardProfile = TestSnapshots.Dashboard(),
            Modules = [TestSnapshots.RiskEngineModule(isEnabled: false, state: AgnosiaModuleState.Disabled)]
        };
        var owner = TestWorkspaceFactory.Create(services);
        var app = TestWorkspaceFactory.CreateApp(owner, Snapshot(Finding("SU-MIC-01", "RECORD_AUDIO")));
        Assert.True(app.ShowRiskSection);
        var changes = new List<string?>();
        app.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        _ = app.RiskReport;
        await owner.EnsureInitializedAsync();
        app.NotifyRiskEngineModuleStateChanged();
        Assert.False(app.ShowRiskSection);
        Assert.Contains(nameof(app.ShowRiskSection), changes);
        Assert.Empty(app.RiskReport.Sections);
        Assert.Empty(app.RiskReport.StatusText);
    }

    [Fact]
    public void Disabled_module_hides_previous_report_and_timestamp()
    {
        var report = AppRiskReportBuilder.Build(Snapshot(Finding("SU-MIC-01", "RECORD_AUDIO")), false, true, true);
        Assert.Empty(report.Sections);
        Assert.Empty(report.Summary);
        Assert.Empty(report.CheckedAtText);
        Assert.Empty(report.StatusText);
    }

    [Fact]
    public void Failed_refresh_keeps_previous_data_and_capture_time()
    {
        var snapshot = Snapshot(Finding("SU-MIC-01", "RECORD_AUDIO"));
        var report = AppRiskReportBuilder.Build(snapshot, true, false, true, TimeZoneInfo.Utc);
        Assert.NotEmpty(report.Sections);
        Assert.Equal("Проверено 28.09.26 11:32", report.CheckedAtText);
        Assert.Contains("предыдущей проверки", report.StatusText);
    }

    [Theory]
    [InlineData(true, false, "Проверяем доступы приложения…")]
    [InlineData(false, true, "Не удалось выполнить проверку")]
    [InlineData(false, false, "Оценка пока недоступна")]
    public void No_result_is_not_a_safe_assessment(bool refreshing, bool failed, string expected)
    {
        var snapshot = Snapshot() with { PermissionRiskAvailable = false };
        var report = AppRiskReportBuilder.Build(snapshot, true, refreshing, failed);
        Assert.Equal(expected, report.StatusText);
        Assert.Empty(report.Summary);
        Assert.Empty(report.LevelText);
        Assert.Empty(report.CheckedAtText);
    }

    [Fact]
    public void Missing_details_preserve_elevated_level()
    {
        var report = AppRiskReportBuilder.Build(Snapshot() with { PermissionRiskLevel = AppPermissionRiskLevel.Critical }, true, false, false);
        Assert.Equal("Критический риск", report.LevelText);
        Assert.Contains("нет подробного описания", report.Summary);
        Assert.DoesNotContain("не обнаружены", report.Summary);
    }

    [Fact]
    public void Incomplete_check_metadata_does_not_leak_to_ui()
    {
        var snapshot = Snapshot() with { PermissionRiskUnavailableChecks = ["android.permission.TEST"] };
        var text = VisibleText(AppRiskReportBuilder.Build(snapshot, true, false, false));
        Assert.Contains("Проверка не выявила опасных возможностей.", text);
        Assert.DoesNotContain("неполная", text);
        Assert.DoesNotContain("недоступна", text);
        Assert.DoesNotContain("TEST", text);
    }

    [Fact]
    public void Protection_is_one_simple_setting_status_without_disclaimer()
    {
        var report = AppRiskReportBuilder.Build(Snapshot() with { IsInternetBlocked = true }, true, false, false);
        Assert.Equal("Для приложения включена блокировка интернета", report.ProtectionText);
    }
}
