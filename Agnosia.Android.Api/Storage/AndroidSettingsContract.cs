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
        if (!Enum.TryParse<VpnAutomationClientKind>(value, true, out var client) || !Enum.IsDefined(client))
            return VpnAutomationClientKind.FlClash;

        return client switch
        {
            VpnAutomationClientKind.V2RayNgFdroid => VpnAutomationClientKind.V2RayNg,
            VpnAutomationClientKind.OlcNgFdroid => VpnAutomationClientKind.OlcNg,
            _ => client
        };
    }

    public static CommandTransportPreference ParseCommandTransportPreference(string? value)
    {
        return Enum.TryParse<CommandTransportPreference>(value, true, out var preference)
               && Enum.IsDefined(preference)
            ? preference
            : CommandTransportPreference.Auto;
    }

    public static string NormalizeTunguskaAutomationToken(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}
