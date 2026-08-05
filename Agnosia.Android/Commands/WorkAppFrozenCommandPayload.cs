using System.Text.Json;

namespace Agnosia.Android.Commands;

internal sealed record WorkAppFrozenCommandPayload(
    string PackageName,
    string LaunchId,
    string Trigger)
{
    private static WorkAppFrozenCommandPayload Empty { get; } = new(string.Empty, string.Empty, string.Empty);

    public static WorkAppFrozenCommandPayload Create(string packageName, string launchId, string trigger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(launchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(trigger);
        return new WorkAppFrozenCommandPayload(packageName, launchId, trigger);
    }

    public string Serialize()
    {
        return JsonSerializer.Serialize(this);
    }

    public static bool TryDeserialize(string? raw, out WorkAppFrozenCommandPayload payload)
    {
        payload = Empty;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<WorkAppFrozenCommandPayload>(raw);
            if (parsed is null
                || string.IsNullOrWhiteSpace(parsed.PackageName)
                || string.IsNullOrWhiteSpace(parsed.LaunchId)
                || string.IsNullOrWhiteSpace(parsed.Trigger))
            {
                return false;
            }

            payload = parsed;
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
}
