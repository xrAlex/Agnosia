using Agnosia.Models;
using Agnosia.ViewModels;
using Xunit;

namespace Agnosia.Unit.ViewModels;

public sealed class DashboardStatusTextFormatterTests
{
    // Проверяет ключи статуса рабочего профиля для основных состояний профиля.
    [Theory]
    [InlineData(WorkProfileStateKind.NoWorkProfile, true, false, "Available")]
    [InlineData(WorkProfileStateKind.Unavailable, false, true, "Unavailable")]
    [InlineData(WorkProfileStateKind.NoWorkProfile, false, false, "NotCreated")]
    [InlineData(WorkProfileStateKind.NoWorkProfile, false, true, "Unavailable")]
    public void GetWorkProfileStatus_returns_expected_status_key(
        WorkProfileStateKind workProfileState,
        bool workProfileAvailable,
        bool hasSetup,
        string expected)
    {
        var actual = DashboardStatusTextFormatter.GetWorkProfileStatus(
            workProfileState,
            workProfileAvailable,
            hasSetup);

        Assert.Equal(expected, actual);
    }

    // Проверяет приоритет headline на overview для загрузки, ошибок setup и доступности профиля.
    [Theory]
    [InlineData(false, true, true, false, true, "Loading")]
    [InlineData(true, false, true, false, true, "NotSupported")]
    [InlineData(true, true, false, false, true, "NotSetup")]
    [InlineData(true, true, true, true, true, "Syncing")]
    [InlineData(true, true, true, false, true, "Active")]
    [InlineData(true, true, true, false, false, "WPUnavailable")]
    public void GetOverviewHeadline_respects_status_priority(
        bool hasLoadedSnapshot,
        bool isSupported,
        bool hasSetup,
        bool isBusy,
        bool workProfileAvailable,
        string expected)
    {
        var actual = DashboardStatusTextFormatter.GetOverviewHeadline(
            hasLoadedSnapshot,
            isSupported,
            hasSetup,
            isBusy,
            workProfileAvailable);

        Assert.Equal(expected, actual);
    }

    // Проверяет общий короткий статус dashboard для error/updating/unstable/ok.
    [Theory]
    [InlineData(true, true, false, true, true, true, "Error")]
    [InlineData(false, false, false, true, true, true, "Updating")]
    [InlineData(false, true, true, true, true, true, "Updating")]
    [InlineData(false, true, false, false, true, true, "Unstable")]
    [InlineData(false, true, false, true, false, true, "Unstable")]
    [InlineData(false, true, false, true, true, false, "Unstable")]
    [InlineData(false, true, false, true, true, true, "Ok")]
    public void GetOverallStatusText_returns_expected_key(
        bool statusIsError,
        bool hasLoadedSnapshot,
        bool isBusy,
        bool isSupported,
        bool hasSetup,
        bool workProfileAvailable,
        string expected)
    {
        var actual = DashboardStatusTextFormatter.GetOverallStatusText(
            statusIsError,
            hasLoadedSnapshot,
            isBusy,
            isSupported,
            hasSetup,
            workProfileAvailable);

        Assert.Equal(expected, actual);
    }

    // Проверяет caption общего статуса с детализацией причин нестабильности.
    [Theory]
    [InlineData(true, true, false, true, true, true, "Error")]
    [InlineData(false, false, false, true, true, true, "Loading")]
    [InlineData(false, true, true, true, true, true, "Syncing")]
    [InlineData(false, true, false, false, true, true, "NotSupported")]
    [InlineData(false, true, false, true, false, true, "NotSetup")]
    [InlineData(false, true, false, true, true, false, "WPUnavailable")]
    [InlineData(false, true, false, true, true, true, "Ok")]
    public void GetOverallStatusCaption_returns_expected_key(
        bool statusIsError,
        bool hasLoadedSnapshot,
        bool isBusy,
        bool isSupported,
        bool hasSetup,
        bool workProfileAvailable,
        string expected)
    {
        var actual = DashboardStatusTextFormatter.GetOverallStatusCaption(
            statusIsError,
            hasLoadedSnapshot,
            isBusy,
            isSupported,
            hasSetup,
            workProfileAvailable);

        Assert.Equal(expected, actual);
    }
}
