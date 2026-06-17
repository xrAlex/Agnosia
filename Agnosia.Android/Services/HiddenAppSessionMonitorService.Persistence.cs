using System.Text.Json;
using System.Text.Json.Serialization;
using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Storage;
using Agnosia.Android.Serialization;
using Android.App;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Services;

public sealed partial class HiddenAppSessionMonitorService
{
    public static bool HasPersistedSessionForScreenLock()
    {
        return TryLoadPersistedSession(out _);
    }

    private static void PersistSession(HiddenAppSessionState? session)
    {
        if (session is null)
        {
            ServiceRegistry.GetRequiredService<LocalStorageManager>().Remove(StorageKeys.HiddenAppActiveSession);
            return;
        }

        ServiceRegistry.GetRequiredService<LocalStorageManager>().SetString(
            StorageKeys.HiddenAppActiveSession,
            JsonSerializer.Serialize(session, AndroidJsonContext.Default.HiddenAppSessionState));
    }

    private static bool TryLoadPersistedSession(out HiddenAppSessionState session)
    {
        var raw = ServiceRegistry.GetRequiredService<LocalStorageManager>().GetString(StorageKeys.HiddenAppActiveSession);
        if (string.IsNullOrWhiteSpace(raw))
        {
            session = HiddenAppSessionState.Empty;
            return false;
        }

        try
        {
            session = JsonSerializer.Deserialize(raw, AndroidJsonContext.Default.HiddenAppSessionState)
                      ?? HiddenAppSessionState.Empty;
            return !string.IsNullOrWhiteSpace(session.PackageName) && session.TaskId >= 0;
        }
        catch (JsonException exception)
        {
            Log.Warn(LogTag, $"Failed to restore hidden-app session: {exception.Message}");
            ServiceRegistry.GetRequiredService<LocalStorageManager>().Remove(StorageKeys.HiddenAppActiveSession);
            session = HiddenAppSessionState.Empty;
            return false;
        }
    }

    private static AndroidAppLaunchResult GetSessionLaunchResult(HiddenAppSessionState session)
    {
        return session.LaunchResult ?? AndroidAppLaunchResult.CommandReceived(session.PackageName, session.DisplayName);
    }

}

internal sealed record HiddenAppSessionState(
    string PackageName,
    string DisplayName,
    int TaskId,
    long StartedAtUnixTimeMilliseconds = 0,
    AndroidAppLaunchResult? LaunchResult = null)
{
    public static HiddenAppSessionState Empty { get; } = new(string.Empty, string.Empty, -1);

    [JsonIgnore] public PendingIntent? ParentFrozenCallback { get; init; }
}
