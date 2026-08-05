using System.Text.Json;

namespace Agnosia.Android.Vpn;

internal static class VpnRestoreOwnershipCodec
{
    public static string Serialize(VpnRestoreOwnershipState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return JsonSerializer.Serialize(
            state,
            VpnRestoreOwnershipJsonContext.Default.VpnRestoreOwnershipState);
    }

    public static bool TryDeserialize(string? raw, out VpnRestoreOwnershipState state)
    {
        state = VpnRestoreOwnershipState.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (!TryGetProperty(document.RootElement, nameof(VpnRestoreOwnershipState.Version), out var versionElement)
                || !versionElement.TryGetInt32(out var version)
                || version != VpnRestoreOwnershipState.CurrentVersion)
            {
                return false;
            }

            var parsed = JsonSerializer.Deserialize(
                raw,
                VpnRestoreOwnershipJsonContext.Default.VpnRestoreOwnershipState);
            if (parsed is null || !IsValid(parsed)) return false;

            state = parsed;
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
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsValid(VpnRestoreOwnershipState state)
    {
        if (state.ActiveOwner is not null && !IsValid(state.ActiveOwner)) return false;
        if (state.PendingOwner is not null && !IsValid(state.PendingOwner)) return false;
        if (!state.RestoreRequired && state.ActiveOwner is not null) return false;
        if (state.AcceptLegacyCallback && (!state.RestoreRequired || state.ActiveOwner is not null)) return false;

        return true;
    }

    private static bool IsValid(VpnRestoreOwner owner)
    {
        return !string.IsNullOrWhiteSpace(owner.LaunchId)
               && !string.IsNullOrWhiteSpace(owner.PackageName);
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
