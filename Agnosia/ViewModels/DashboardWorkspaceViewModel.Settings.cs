using Agnosia.Infrastructure;
using Agnosia.Models;

namespace Agnosia.ViewModels;

public partial class DashboardWorkspaceViewModel
{
    partial void OnSelectedThemeChanged(AppThemeKind value)
    {
        if (!_isApplyingSnapshot) AppThemeManager.Apply(value);

        QueueSettingsSave();
    }

    partial void OnLoggingEnabledChanged(bool value)
    {
        if (value)
        {
            QueueSettingsSave();
            return;
        }

        IsLogWindowOpen = false;
        ClearLogs();
        QueueSettingsSave();
    }

    partial void OnShowAllAppsChanged(bool value) => QueueSettingsSave();

    partial void OnDisableVpnBeforeWorkLaunchChanged(bool value) => QueueSettingsSave();

    partial void OnCrossProfileFileShuttleEnabledChanged(bool value) => QueueSettingsSave();

    partial void OnEnableVpnAfterWorkFreezeChanged(bool value) => QueueSettingsSave();

    partial void OnVpnAfterWorkFreezeClientChanged(VpnAutomationClientKind value)
    {
        foreach (var option in VpnAfterFreezeClientOptions) option.NotifySelectionChanged();
        SelectedModule?.NotifyVpnSettingsChanged();

        QueueSettingsSave();
    }

    partial void OnTunguskaAutomationTokenChanged(string value)
    {
        SelectedModule?.NotifyVpnSettingsChanged();
        QueueSettingsSave();
    }

    private AppSettingsSnapshot CaptureSettingsSnapshot()
    {
        return new AppSettingsSnapshot(
            ShowAllApps,
            DisableVpnBeforeWorkLaunch,
            CrossProfileFileShuttleEnabled,
            LoggingEnabled,
            SelectedTheme,
            EnableVpnAfterWorkFreeze,
            VpnAfterWorkFreezeClient,
            TunguskaAutomationToken);
    }

    private void QueueSettingsSave() => _settingsSaveCoordinator.Queue();

    internal bool IsVpnAfterFreezeClientSelected(VpnAutomationClientKind kind)
    {
        return VpnAfterWorkFreezeClient == kind;
    }

    internal void SelectVpnAfterFreezeClient(VpnAutomationClientKind kind)
    {
        VpnAfterWorkFreezeClient = kind;
    }

    private VpnAutomationClientOptionViewModel[] CreateVpnAfterFreezeClientOptions()
    {
        return
        [
            new(this, VpnAutomationClientKind.FlClash, "FlClash"),
            new(this, VpnAutomationClientKind.ClashMeta, "Clash Meta"),
            new(this, VpnAutomationClientKind.Happ, "Happ"),
            new(this, VpnAutomationClientKind.Tunguska, "Tunguska"),
            new(this, VpnAutomationClientKind.Incy, "INCY"),
            new(this, VpnAutomationClientKind.Exclave, "Exclave"),
            new(this, VpnAutomationClientKind.Husi, "husi"),
            new(this, VpnAutomationClientKind.NekoBoxPlus, "NekoBox+")
        ];
    }

    private void SetSettingsSaveStatus(bool isError, string? message)
    {
        StatusIsError = isError;
        if (!string.IsNullOrWhiteSpace(message)) StatusMessage = message;
    }
}
