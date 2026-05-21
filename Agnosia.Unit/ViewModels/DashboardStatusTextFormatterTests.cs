using Agnosia.Models;
using Agnosia.ViewModels;
using Xunit;

namespace Agnosia.Unit.ViewModels;

public sealed class DashboardStatusTextFormatterTests
{
    // Проверяет ключи статуса рабочего профиля для основных состояний профиля.
    [Theory]
    [InlineData(WorkProfileStateKind.NoWorkProfile, true, false, "Available")]
    [InlineData(WorkProfileStateKind.ProvisioningInProgress, false, false, "Pending")]
    [InlineData(WorkProfileStateKind.WorkProfileQuietMode, false, true, "QuietMode")]
    [InlineData(WorkProfileStateKind.WorkProfileUnavailable, false, true, "Disabled")]
    [InlineData(WorkProfileStateKind.WorkProfileCommandTargetUnavailable, false, true, "Unavailable")]
    [InlineData(WorkProfileStateKind.WorkProfileCommandChannelUnavailable, false, true, "Unavailable")]
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

    // Проверяет приоритет headline на overview для загрузки, ошибок setup и состояния профиля.
    [Theory]
    [InlineData(false, true, true, false, true, WorkProfileStateKind.AppIsProfileOwner, "Loading")]
    [InlineData(true, false, true, false, true, WorkProfileStateKind.AppIsProfileOwner, "NotSupported")]
    [InlineData(true, true, false, false, true, WorkProfileStateKind.AppIsProfileOwner, "NotSetup")]
    [InlineData(true, true, true, true, true, WorkProfileStateKind.AppIsProfileOwner, "Syncing")]
    [InlineData(true, true, true, false, true, WorkProfileStateKind.AppIsProfileOwner, "Active")]
    [InlineData(true, true, true, false, false, WorkProfileStateKind.WorkProfileQuietMode, "WPQuietMode")]
    [InlineData(true, true, true, false, false, WorkProfileStateKind.WorkProfileUnavailable, "WPDisabled")]
    [InlineData(true, true, true, false, false, WorkProfileStateKind.WorkProfileCommandTargetUnavailable, "WPUnavailable")]
    [InlineData(true, true, true, false, false, WorkProfileStateKind.NoWorkProfile, "WPUnavailable")]
    public void GetOverviewHeadline_respects_status_priority(
        bool hasLoadedSnapshot,
        bool isSupported,
        bool hasSetup,
        bool isBusy,
        bool workProfileAvailable,
        WorkProfileStateKind workProfileState,
        string expected)
    {
        var actual = DashboardStatusTextFormatter.GetOverviewHeadline(
            hasLoadedSnapshot,
            isSupported,
            hasSetup,
            isBusy,
            workProfileAvailable,
            workProfileState);

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
    [InlineData(true, true, false, true, true, true, WorkProfileStateKind.AppIsProfileOwner, "Error")]
    [InlineData(false, false, false, true, true, true, WorkProfileStateKind.AppIsProfileOwner, "Loading")]
    [InlineData(false, true, true, true, true, true, WorkProfileStateKind.AppIsProfileOwner, "Syncing")]
    [InlineData(false, true, false, false, true, true, WorkProfileStateKind.AppIsProfileOwner, "NotSupported")]
    [InlineData(false, true, false, true, false, true, WorkProfileStateKind.AppIsProfileOwner, "NotSetup")]
    [InlineData(false, true, false, true, true, false, WorkProfileStateKind.WorkProfileQuietMode, "WPQuietMode")]
    [InlineData(false, true, false, true, true, false, WorkProfileStateKind.WorkProfileUnavailable, "WPDisabled")]
    [InlineData(false, true, false, true, true, false, WorkProfileStateKind.WorkProfileCommandChannelUnavailable, "WPUnavailable")]
    [InlineData(false, true, false, true, true, false, WorkProfileStateKind.NoWorkProfile, "WPUnavailable")]
    [InlineData(false, true, false, true, true, true, WorkProfileStateKind.AppIsProfileOwner, "Ok")]
    public void GetOverallStatusCaption_returns_expected_key(
        bool statusIsError,
        bool hasLoadedSnapshot,
        bool isBusy,
        bool isSupported,
        bool hasSetup,
        bool workProfileAvailable,
        WorkProfileStateKind workProfileState,
        string expected)
    {
        var actual = DashboardStatusTextFormatter.GetOverallStatusCaption(
            statusIsError,
            hasLoadedSnapshot,
            isBusy,
            isSupported,
            hasSetup,
            workProfileAvailable,
            workProfileState);

        Assert.Equal(expected, actual);
    }
}
