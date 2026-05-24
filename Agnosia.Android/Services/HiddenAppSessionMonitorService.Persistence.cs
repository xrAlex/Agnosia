using System.Text.Json;
using System.Text.Json.Serialization;
using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Storage;
using Android.App;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Services;

public sealed partial class HiddenAppSessionMonitorService
{
    private static void PersistSession(HiddenAppSessionState? session)
    {
        if (session is null)
        {
            LocalStorageManager.Instance.Remove(StorageKeys.HiddenAppActiveSession);
            return;
        }

        LocalStorageManager.Instance.SetString(
            StorageKeys.HiddenAppActiveSession,
            JsonSerializer.Serialize(session));
    }

    private static bool TryLoadPersistedSession(out HiddenAppSessionState session)
    {
        var raw = LocalStorageManager.Instance.GetString(StorageKeys.HiddenAppActiveSession);
        if (string.IsNullOrWhiteSpace(raw))
        {
            session = HiddenAppSessionState.Empty;
            return false;
        }

        try
        {
            session = JsonSerializer.Deserialize<HiddenAppSessionState>(raw) ?? HiddenAppSessionState.Empty;
            return !string.IsNullOrWhiteSpace(session.PackageName) && session.TaskId >= 0;
        }
        catch (JsonException exception)
        {
            Log.Warn(LogTag, $"Failed to restore hidden-app session: {exception.Message}");
            LocalStorageManager.Instance.Remove(StorageKeys.HiddenAppActiveSession);
            session = HiddenAppSessionState.Empty;
            return false;
        }
    }

    private static AndroidAppLaunchResult GetSessionLaunchResult(HiddenAppSessionState session)
    {
        return session.LaunchResult ?? AndroidAppLaunchResult.CommandReceived(session.PackageName, session.DisplayName);
    }

    private sealed record HiddenAppSessionState(
        string PackageName,
        string DisplayName,
        int TaskId,
        long StartedAtUnixTimeMilliseconds = 0,
        AndroidAppLaunchResult? LaunchResult = null)
    {
        public static HiddenAppSessionState Empty { get; } = new(string.Empty, string.Empty, -1);

        [JsonIgnore] public PendingIntent? ParentFrozenCallback { get; init; }
    }
}
