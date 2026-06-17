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
            new VpnAutomationClientOptionViewModel(this, VpnAutomationClientKind.FlClash, "FlClash"),
            new VpnAutomationClientOptionViewModel(this, VpnAutomationClientKind.ClashMeta, "Clash Meta"),
            new VpnAutomationClientOptionViewModel(this, VpnAutomationClientKind.Happ, "Happ"),
            new VpnAutomationClientOptionViewModel(this, VpnAutomationClientKind.Tunguska, "Tunguska"),
            new VpnAutomationClientOptionViewModel(this, VpnAutomationClientKind.Incy, "INCY"),
            new VpnAutomationClientOptionViewModel(this, VpnAutomationClientKind.Exclave, "Exclave"),
            new VpnAutomationClientOptionViewModel(this, VpnAutomationClientKind.Husi, "husi"),
            new VpnAutomationClientOptionViewModel(this, VpnAutomationClientKind.NekoBoxPlus, "NekoBox+")
        ];
    }

    private void SetSettingsSaveStatus(bool isError, string? message)
    {
        StatusIsError = isError;
        if (!string.IsNullOrWhiteSpace(message)) StatusMessage = message;
    }
}
