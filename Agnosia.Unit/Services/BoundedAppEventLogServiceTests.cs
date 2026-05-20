using Agnosia.Models;
using Agnosia.Services;
using Xunit;

namespace Agnosia.Unit.Services;

public sealed class BoundedAppEventLogServiceTests
{
    // Проверяет, что журнал не принимает нулевую или отрицательную емкость.
    [Fact]
    public void Constructor_rejects_non_positive_capacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedAppEventLogService(0));
    }

    // Проверяет состояние пустого журнала и текст-заглушку для UI.
    [Fact]
    public void Empty_log_exposes_stable_placeholder_state()
    {
        var service = new BoundedAppEventLogService(3);

        Assert.Equal("Сообщений: 0 / 3", service.Summary);
        Assert.Equal("Журнал пока пуст.", service.Output);
        Assert.Equal(["Журнал пока пуст."], service.Lines);
    }

    // Проверяет сортировку, форматирование и признак изменения при импорте логов.
    [Fact]
    public void ImportPlatformLogs_sorts_formats_and_reports_changes()
    {
        var service = new BoundedAppEventLogService(10);

        var changed = service.ImportPlatformLogs(
        [
            Log("b", 10, AppLogLevel.Warning, ProfileKind.Personal, "", "late warning"),
            Log("c", 9, AppLogLevel.Error, ProfileKind.Work, "Policy", "work error"),
            Log("a", 9, AppLogLevel.Debug, ProfileKind.Personal, "Ui", "debug info")
        ]);

        Assert.True(changed);
        Assert.Equal(
        [
            "09:00:00  DBG  [PER/Ui] debug info",
            "09:00:00  ERR  [WRK/Policy] work error",
            "10:00:00  WRN  [PER] late warning"
        ], service.Lines);
    }

    // Проверяет, что повторный platform log id не добавляет дубликат.
    [Fact]
    public void ImportPlatformLogs_ignores_duplicate_platform_ids()
    {
        var service = new BoundedAppEventLogService(10);

        Assert.True(service.ImportPlatformLogs([Log("same-id", 9, AppLogLevel.Information, ProfileKind.Personal, "", "first")]));

        var changed = service.ImportPlatformLogs(
        [
            Log("same-id", 10, AppLogLevel.Error, ProfileKind.Work, "Policy", "duplicate")
        ]);

        Assert.False(changed);
        Assert.Single(service.Lines);
        Assert.Contains("first", service.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("duplicate", service.Output, StringComparison.Ordinal);
    }

    // Проверяет, что журнал удаляет самые старые записи при превышении емкости.
    [Fact]
    public void ImportPlatformLogs_trims_oldest_entries_to_capacity()
    {
        var service = new BoundedAppEventLogService(2);

        service.ImportPlatformLogs(
        [
            Log("1", 8, AppLogLevel.Information, ProfileKind.Personal, "", "old"),
            Log("2", 9, AppLogLevel.Information, ProfileKind.Personal, "", "middle"),
            Log("3", 10, AppLogLevel.Information, ProfileKind.Personal, "", "new")
        ]);

        Assert.Equal(
        [
            "09:00:00  INF  [PER] middle",
            "10:00:00  INF  [PER] new"
        ], service.Lines);
        Assert.Equal("Сообщений: 2 / 2", service.Summary);
    }

    // Проверяет, что Clear очищает строки и сбрасывает защиту от дублей.
    [Fact]
    public void Clear_resets_entries_and_duplicate_tracking()
    {
        var service = new BoundedAppEventLogService(2);
        var log = Log("id", 9, AppLogLevel.Information, ProfileKind.Personal, "", "message");

        service.ImportPlatformLogs([log]);
        service.Clear();
        var changedAfterClear = service.ImportPlatformLogs([log]);

        Assert.True(changedAfterClear);
        Assert.Single(service.Lines);
    }

    private static AppLogEntry Log(
        string id,
        int hour,
        AppLogLevel level,
        ProfileKind profile,
        string tag,
        string message)
    {
        return new AppLogEntry(
            id,
            new DateTimeOffset(2026, 1, 1, hour, 0, 0, TimeSpan.Zero),
            profile,
            level,
            tag,
            message);
    }
}
