using System.Text.Json;
using Agnosia.Android.Api.Commands;

namespace Agnosia.Android.Services;

internal static class HiddenAppSessionStoreCodec
{
    public static string Serialize(HiddenAppSessionStoreState state)
    {
        return JsonSerializer.Serialize(state, HiddenAppSessionStoreJsonContext.Default.HiddenAppSessionStoreState);
    }

    public static bool TryDeserialize(string? raw, out HiddenAppSessionStoreState state)
    {
        state = HiddenAppSessionStoreState.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (TryGetProperty(document.RootElement, nameof(HiddenAppSessionStoreState.Version), out var versionElement))
            {
                if (!versionElement.TryGetInt32(out var version))
                {
                    return false;
                }

                if (version == 1)
                {
                    var versionOne = JsonSerializer.Deserialize(
                        raw,
                        HiddenAppSessionStoreJsonContext.Default.LegacyHiddenAppSessionStoreStateV1);
                    if (versionOne?.PendingHides is null
                        || (versionOne.ActiveSession is not null && !IsValid(versionOne.ActiveSession))
                        || versionOne.PendingHides.Any(pending => pending is null || !IsValid(pending.Session)))
                    {
                        return false;
                    }

                    state = new HiddenAppSessionStoreState(
                        versionOne.ActiveSession,
                        versionOne.PendingHides,
                        []);
                    return true;
                }

                if (version != HiddenAppSessionStoreState.CurrentVersion) return false;

                var parsed = JsonSerializer.Deserialize(
                    raw,
                    HiddenAppSessionStoreJsonContext.Default.HiddenAppSessionStoreState);
                if (parsed is null || !IsValid(parsed)) return false;

                state = parsed;
                return true;
            }

            var legacy = JsonSerializer.Deserialize(
                raw,
                HiddenAppSessionStoreJsonContext.Default.LegacyHiddenAppSessionState);
            if (legacy is null
                || string.IsNullOrWhiteSpace(legacy.PackageName)
                || legacy.TaskId < 0)
            {
                return false;
            }

            var sessionId = $"legacy:{legacy.PackageName}:{legacy.TaskId}:{legacy.StartedAtUnixTimeMilliseconds}";
            state = new HiddenAppSessionStoreState(
                new HiddenAppSessionState(
                    sessionId,
                    legacy.PackageName,
                    string.IsNullOrWhiteSpace(legacy.DisplayName) ? legacy.PackageName : legacy.DisplayName,
                    legacy.TaskId,
                    legacy.StartedAtUnixTimeMilliseconds,
                    legacy.LaunchResult),
                [],
                []);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsValid(HiddenAppSessionStoreState state)
    {
        return state.PendingHides is not null
               && state.PendingParentNotifications is not null
               && (state.ActiveSession is null || IsValid(state.ActiveSession))
               && state.PendingHides.All(pending => pending is not null && IsValid(pending.Session))
               && state.PendingParentNotifications.All(IsValid);
    }

    private static bool IsValid(HiddenAppPendingParentNotificationState notification)
    {
        return notification is not null
               && IsValid(notification.Session)
               && !string.IsNullOrWhiteSpace(notification.Session.ParentCallbackLaunchId)
               && !string.IsNullOrWhiteSpace(notification.Reason)
               && notification.FailedAttempts >= 0;
    }

    private static bool IsValid(HiddenAppSessionState session)
    {
        return !string.IsNullOrWhiteSpace(session.SessionId)
               && !string.IsNullOrWhiteSpace(session.PackageName)
               && session.TaskId >= 0;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)) continue;

            value = property.Value;
            return true;
        }

        value = default;
        return false;
    }
}

internal sealed record LegacyHiddenAppSessionState(
    string PackageName,
    string DisplayName,
    int TaskId,
    long StartedAtUnixTimeMilliseconds = 0,
    AndroidAppLaunchResult? LaunchResult = null);

internal sealed record LegacyHiddenAppSessionStoreStateV1(
    HiddenAppSessionState? ActiveSession,
    HiddenAppPendingHideState[] PendingHides,
    int Version);
