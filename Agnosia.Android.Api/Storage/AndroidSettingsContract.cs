using Agnosia.Models;

namespace Agnosia.Android.Api.Storage;

public static class AndroidSettingsContract
{
    public static AppThemeKind ParseAppTheme(string? value)
    {
        return Enum.TryParse<AppThemeKind>(value, true, out var theme)
            ? theme
            : AppThemeKind.Agnosia;
    }

    public static VpnAutomationClientKind ParseVpnAfterWorkFreezeClient(string? value)
    {
        return Enum.TryParse<VpnAutomationClientKind>(value, true, out var client)
            ? client
            : VpnAutomationClientKind.FlClash;
    }

    public static string NormalizeTunguskaAutomationToken(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}
