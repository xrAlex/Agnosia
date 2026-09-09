using System.Text.Json.Serialization;
using Android.Content;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Services;

public sealed partial class HiddenAppSessionMonitorService
{
    private static readonly Lock PersistedStateSync = new();

    public static bool HasPersistedSessionForScreenLock()
    {
        return TryLoadPersistedState(out var state) && !state.IsEmpty;
    }

    internal static string? GetPersistedLaunchId(string packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName)) return null;

        lock (PersistedStateSync)
        {
            return TryLoadPersistedStateCore(out var state)
                ? state.GetPersistedLaunchId(packageName)
                : null;
        }
    }

    internal static bool IsKnownLaunch(string packageName, string launchId)
    {
        TryLoadPersistedState(out var state);
        return new[] { state.ActiveSession, state.ActiveSession?.PreviousSession }
            .Concat(state.PendingHides.Select(item => item.Session))
            .Concat(state.PendingParentNotifications.Select(item => item.Session))
            .Any(session => session?.PackageName == packageName && session.ParentCallbackLaunchId == launchId);
    }

    internal static bool ConfirmParentNotification(string packageName, string launchId)
    {
        if (string.IsNullOrWhiteSpace(packageName) || string.IsNullOrWhiteSpace(launchId)) return false;

        lock (PersistedStateSync)
        {
            TryLoadPersistedStateCore(out var current);
            var updated = current.ConfirmParentNotification(packageName, launchId);
            if (ReferenceEquals(updated, current))
                return current.GetPersistedLaunchId(packageName) != launchId;

            PersistStateCore(updated);
            return true;
        }
    }

    // Caller holds the operation gate and has confirmed hidden/missing state.
    internal static void PrepareParentNotification(string packageName, string launchId)
    {
        UpdatePersistedState(state =>
        {
            var now = DateTimeOffset.UtcNow;
            if (state.ActiveSession is { } active && active.PackageName == packageName
                && active.ParentCallbackLaunchId == launchId)
                state = state.BeginCompletion(active.SessionId, "recovery_confirmed_hidden", now);
            foreach (var pending in state.PendingHides.Where(item => item.Session.PackageName == packageName
                         && item.Session.ParentCallbackLaunchId == launchId).ToArray())
                state = state.ConfirmHidden(pending.Session.SessionId, now);
            return state;
        });
    }

    internal static HiddenAppSessionState ReserveLaunchSession(
        Context context,
        string packageName,
        string displayName,
        int taskId,
        AndroidAppLaunchResult launchResult,
        PendingIntent? parentFrozenCallback,
        string? parentCallbackLaunchId)
    {
        var session = HiddenAppSessionState.Create(
                packageName,
                displayName,
                taskId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                launchResult)
            with
            {
                ParentFrozenCallback = parentFrozenCallback,
                ParentCallbackLaunchId = parentCallbackLaunchId
        };
        UpdatePersistedState(state =>
        {
            session = session with { PreviousSession = state.ActiveSession is { } previous
                ? previous with { PreviousSession = null } : null };
            return state.StartOrReplace(session, DateTimeOffset.UtcNow);
        });
        if (parentFrozenCallback is not null && !string.IsNullOrWhiteSpace(parentCallbackLaunchId)
            && !WorkVpnRecoveryAlarm.Schedule(context, typeof(Receivers.WorkVpnRecoveryReceiver),
                packageName, parentCallbackLaunchId, parentFrozenCallback))
        {
            UpdatePersistedState(state => state.CancelActive(session.SessionId));
            throw new InvalidOperationException("Android did not retain the VPN recovery callback.");
        }
        if (!EnsurePendingHideRetryRunning(context))
        {
            UpdatePersistedState(state => state.CancelActive(session.SessionId));
            throw new InvalidOperationException("Android did not accept durable hidden-app launch monitoring.");
        }

        return session;
    }

    internal static HiddenAppSessionState ReserveTemporaryVisibility(
        Context context,
        string packageName,
        string displayName)
    {
        var session = HiddenAppSessionState.Create(
            packageName,
            displayName,
            0,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AndroidAppLaunchResult.CommandReceived(packageName, displayName));
        UpdatePersistedState(state => state.ReservePendingHide(
            session,
            HiddenAppSessionStoreState.TemporaryPolicyVisibilityReason,
            DateTimeOffset.UtcNow));
        if (!EnsurePendingHideRetryRunning(context))
        {
            UpdatePersistedState(state => state.ConfirmHidden(session.SessionId, DateTimeOffset.UtcNow));
            throw new InvalidOperationException("Android did not accept temporary-visibility recovery monitoring.");
        }

        return session;
    }

    internal static void CompleteTemporaryVisibility(
        Context context,
        string sessionId,
        bool hiddenConfirmed)
    {
        var updated = UpdatePersistedState(state => hiddenConfirmed
            ? state.ConfirmHidden(sessionId, DateTimeOffset.UtcNow)
            : state.RecordHideFailure(sessionId, DateTimeOffset.UtcNow));
        if (!hiddenConfirmed && updated.PendingHides.Any(pending => string.Equals(
                pending.Session.SessionId,
                sessionId,
                StringComparison.Ordinal)))
        {
            EnsurePendingHideRetryRunning(context);
        }
    }

    internal static void CancelReservedLaunch(string sessionId)
    {
        var updated = UpdatePersistedState(state => state.CancelActive(sessionId));
        if (updated.RequiresPackageMonitoring)
            EnsurePendingHideRetryRunning(global::Android.App.Application.Context);
    }

    internal static void CompleteReservedLaunch(
        Context context,
        string sessionId,
        string reason,
        bool hiddenConfirmed)
    {
        var updated = UpdatePersistedState(state =>
        {
            var previous = state.ActiveSession?.SessionId == sessionId ? state.ActiveSession.PreviousSession : null;
            var completing = state.BeginCompletion(sessionId, reason, DateTimeOffset.UtcNow);
            if (previous is not null) completing = completing.RestorePreviousSession(previous);
            return hiddenConfirmed
                ? completing.ConfirmHidden(sessionId, DateTimeOffset.UtcNow)
                : completing.RecordHideFailure(sessionId, DateTimeOffset.UtcNow);
        });
        if (updated.RequiresPackageMonitoring)
        {
            EnsurePendingHideRetryRunning(context);
        }
    }

    private static bool TryLoadPersistedState(out HiddenAppSessionStoreState state)
    {
        lock (PersistedStateSync)
        {
            return TryLoadPersistedStateCore(out state);
        }
    }

    private static HiddenAppSessionStoreState UpdatePersistedState(
        Func<HiddenAppSessionStoreState, HiddenAppSessionStoreState> update)
    {
        lock (PersistedStateSync)
        {
            TryLoadPersistedStateCore(out var current);
            var updated = update(current);
            if (ReferenceEquals(updated, current)) return current;

            PersistStateCore(updated);
            return updated;
        }
    }

    private static bool TryLoadPersistedStateCore(out HiddenAppSessionStoreState state)
    {
        var raw = ServiceRegistry.GetRequiredService<LocalStorageManager>().GetString(StorageKeys.HiddenAppActiveSession);
        if (string.IsNullOrWhiteSpace(raw))
        {
            state = HiddenAppSessionStoreState.Empty;
            return false;
        }

        if (HiddenAppSessionStoreCodec.TryDeserialize(raw, out state))
        {
            return !state.IsEmpty;
        }

        Log.Warn(LogTag, "Failed to restore hidden-app session state: payload is invalid.");
        ServiceRegistry.GetRequiredService<LocalStorageManager>().RemoveDurably(StorageKeys.HiddenAppActiveSession);
        state = HiddenAppSessionStoreState.Empty;
        return false;
    }

    private static void PersistStateCore(HiddenAppSessionStoreState state)
    {
        if (state.IsEmpty)
        {
            ServiceRegistry.GetRequiredService<LocalStorageManager>().RemoveDurably(StorageKeys.HiddenAppActiveSession);
            return;
        }

        ServiceRegistry.GetRequiredService<LocalStorageManager>().SetStringDurably(
            StorageKeys.HiddenAppActiveSession,
            HiddenAppSessionStoreCodec.Serialize(state));
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
