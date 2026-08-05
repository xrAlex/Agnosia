using System.Text.Json.Serialization;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Services;

public sealed partial class HiddenAppSessionMonitorService
{
    public static bool HasPersistedSessionForScreenLock()
    {
        return TryLoadPersistedState(out var state) && !state.IsEmpty;
    }

    private static void PersistState(HiddenAppSessionStoreState state)
    {
        if (state.IsEmpty)
        {
            ServiceRegistry.GetRequiredService<LocalStorageManager>().Remove(StorageKeys.HiddenAppActiveSession);
            return;
        }

        ServiceRegistry.GetRequiredService<LocalStorageManager>().SetString(
            StorageKeys.HiddenAppActiveSession,
            HiddenAppSessionStoreCodec.Serialize(state));
    }

    private static bool TryLoadPersistedState(out HiddenAppSessionStoreState state)
    {
        var raw = ServiceRegistry.GetRequiredService<LocalStorageManager>().GetString(StorageKeys.HiddenAppActiveSession);
        if (string.IsNullOrWhiteSpace(raw))
        {
            state = HiddenAppSessionStoreState.Empty;
            return false;
        }

        if (HiddenAppSessionStoreCodec.TryDeserialize(raw, out state))
        {
            PersistState(state);
            return !state.IsEmpty;
        }

        Log.Warn(LogTag, "Failed to restore hidden-app session state: payload is invalid.");
        ServiceRegistry.GetRequiredService<LocalStorageManager>().Remove(StorageKeys.HiddenAppActiveSession);
        state = HiddenAppSessionStoreState.Empty;
        return false;
    }

    private static AndroidAppLaunchResult GetSessionLaunchResult(HiddenAppSessionState session)
    {
        return session.LaunchResult ?? AndroidAppLaunchResult.CommandReceived(session.PackageName, session.DisplayName);
    }

}

internal sealed partial record HiddenAppSessionState
{
    [JsonIgnore] public PendingIntent? ParentFrozenCallback { get; init; }
}
