using Agnosia.Android.Api.Commands;

namespace Agnosia.Android.Commands;

internal sealed class ProviderReplayStore
{
    private readonly Lock _sync = new();
    private readonly Dictionary<Guid, Entry> _entries = [];

    public Task<ProviderCommandMessage> ExecuteAsync(ProviderCommandMessage request,
        Func<Task<ProviderCommandMessage>> execute, long now)
    {
        var fingerprint = ProviderCommandProtocol.Fingerprint(request);
        lock (_sync)
        {
            foreach (var id in _entries.Where(pair => pair.Value.Expires < now && pair.Value.Result.IsCompleted)
                         .Select(pair => pair.Key).ToArray()) _entries.Remove(id);
            if (_entries.TryGetValue(request.CorrelationId, out var previous))
                return previous.Fingerprint == fingerprint ? previous.Result : Task.FromResult(
                    ProviderCommandProtocol.Reply(request, false, null, "replay_conflict", "Request identity was reused."));
            if (_entries.Count >= 128)
                return Task.FromResult(ProviderCommandProtocol.Reply(request, false, null, "provider_busy", "Provider replay window is full."));
            // Never evict a live entry or one whose signed request could still be accepted.
            var task = Task.Run(execute);
            _entries.Add(request.CorrelationId, new Entry(fingerprint, Math.Max(request.Deadline, request.Timestamp + 30_000), task));
            return task;
        }
    }

    private sealed record Entry(string Fingerprint, long Expires, Task<ProviderCommandMessage> Result);
}
