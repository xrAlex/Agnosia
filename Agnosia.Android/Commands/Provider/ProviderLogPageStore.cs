using System.Text.Json;
using System.Text.Json.Serialization;
using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Serialization;
using Agnosia.Models;

namespace Agnosia.Android.Commands;

internal sealed record ProviderLogPageRequest(string PageToken, int Offset);
internal sealed record ProviderLogPageResponse(
    [property: JsonPropertyName(AndroidCommandContract.ResultLogsJson)] string LogsJson,
    int NextOffset, bool HasMore);

internal sealed class ProviderLogPageStore
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, Snapshot> _snapshots = new(StringComparer.Ordinal);

    public ProviderLogPageResponse Read(ProviderLogPageRequest request, Func<IReadOnlyList<AppLogEntry>> load, long now)
    {
        if (!Guid.TryParse(request.PageToken, out _) || request.Offset < 0) throw new InvalidDataException("Invalid log page request.");
        IReadOnlyList<AppLogEntry> entries;
        lock (_sync)
        {
            foreach (var key in _snapshots.Where(pair => pair.Value.Expires <= now).Select(pair => pair.Key).ToArray())
                _snapshots.Remove(key);
            if (!_snapshots.TryGetValue(request.PageToken, out var snapshot))
            {
                if (request.Offset != 0) throw new InvalidOperationException("Log snapshot expired.");
                if (_snapshots.Count >= 4) throw new InvalidOperationException("Too many log snapshots.");
                snapshot = new Snapshot(now + 60_000, load().ToArray());
                _snapshots.Add(request.PageToken, snapshot);
            }
            entries = snapshot.Entries;
        }
        if (request.Offset > entries.Count) throw new InvalidDataException("Invalid log page offset.");
        var items = new List<AppLogEntry>();
        var result = new ProviderLogPageResponse("[]", request.Offset, request.Offset < entries.Count);
        for (var i = request.Offset; i < entries.Count; i++)
        {
            items.Add(entries[i]);
            var candidate = new ProviderLogPageResponse(
                JsonSerializer.Serialize(items, AndroidApiJsonContext.Default.ListAppLogEntry), i + 1, i + 1 < entries.Count);
            if (!Fits(candidate))
            {
                if (items.Count == 1) throw new InvalidDataException("One log entry exceeds the provider budget.");
                break;
            }
            result = candidate;
        }
        if (!result.HasMore)
            lock (_sync) _snapshots.Remove(request.PageToken);
        return result;
    }

    public static bool Fits(ProviderLogPageResponse page)
    {
        var largestEnvelope = new ProviderCommandMessage(ProviderCommandProtocol.Version, Guid.NewGuid(), "QueryLogs",
            int.MaxValue, int.MaxValue, new string('F', 64), long.MaxValue, long.MaxValue, true, true,
            JsonSerializer.Serialize(page), null, new string('x', 1024), new string('F', 64));
        try { ProviderCommandProtocol.Serialize(largestEnvelope); return true; }
        catch (InvalidDataException) { return false; }
    }

    private sealed record Snapshot(long Expires, IReadOnlyList<AppLogEntry> Entries);
}
