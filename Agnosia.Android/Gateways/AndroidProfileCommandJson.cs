using System.Text.Json;
using Agnosia.Models;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Gateways;

internal static class AndroidProfileCommandJson
{
    private const string LogTag = "AgnosiaProfileCommand";

    public static List<AppServiceModel>? DeserializeAppServiceModelsResult(string? raw, string description)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        try
        {
            return JsonSerializer.Deserialize(raw, AndroidApiJsonContext.Default.ListAppServiceModel);
        }
        catch (JsonException exception)
        {
            Log.Warn(LogTag, $"Failed to deserialize {description}: {exception.Message}");
            return null;
        }
    }

    public static List<AppLogEntry>? DeserializeAppLogEntriesResult(string? raw, string description)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        try
        {
            return JsonSerializer.Deserialize(raw, AndroidApiJsonContext.Default.ListAppLogEntry);
        }
        catch (JsonException exception)
        {
            Log.Warn(LogTag, $"Failed to deserialize {description}: {exception.Message}");
            return null;
        }
    }
}
