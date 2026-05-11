namespace Agnosia.Models;

public sealed record AppSettingsSnapshot(
    bool ShowAllApps,
    bool BlockContactsSearching,
    bool DisableVpnBeforeWorkLaunch,
    bool LoggingEnabled,
    AppThemeKind Theme = AppThemeKind.Dark,
    bool EnableVpnAfterWorkFreeze = false,
    VpnAutomationClientKind VpnAfterWorkFreezeClient = VpnAutomationClientKind.FlClash,
    string TunguskaAutomationToken = "")
{
    public static AppSettingsSnapshot Default { get; } = new(false, true, false, true);
}
