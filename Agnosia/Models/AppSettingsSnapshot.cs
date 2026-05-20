namespace Agnosia.Models;

public sealed record AppSettingsSnapshot(
    bool ShowAllApps,
    bool DisableVpnBeforeWorkLaunch,
    bool LoggingEnabled,
    AppThemeKind Theme = AppThemeKind.Dark,
    bool EnableVpnAfterWorkFreeze = false,
    VpnAutomationClientKind VpnAfterWorkFreezeClient = VpnAutomationClientKind.FlClash,
    string TunguskaAutomationToken = "")
{
    public static AppSettingsSnapshot Default { get; } = new(false, false, true);
}
