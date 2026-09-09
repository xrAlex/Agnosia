namespace Agnosia.Android.Files;

internal sealed class AgnosiaFileShuttleListingStore
{
    private const int MaxSnapshots = 4;
    private const long MaxSnapshotCharacters = 2_000_000;
    private readonly Dictionary<string, Snapshot> _snapshots = [];

    // The service's single Handler owns the store.
    public AgnosiaFileShuttlePayloadPage ReadPage(string path, string? token, int offset,
        int maxItems, int maxBytes, Func<IReadOnlyList<AgnosiaFileShuttleDocumentInfo>> load)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var key in _snapshots.Where(entry => entry.Value.ExpiresAt <= now).Select(entry => entry.Key).ToArray())
            _snapshots.Remove(key);

        Snapshot snapshot;
        if (string.IsNullOrEmpty(token))
        {
            if (offset != 0) throw new InvalidOperationException("Directory snapshot token is missing.");
            var documents = load();
            if (documents.Sum(item => (long)item.DocumentId.Length + item.DisplayName.Length + item.MimeType.Length + 64) > MaxSnapshotCharacters)
                throw new InvalidOperationException("Directory exceeds the File Shuttle snapshot size limit.");
            if (_snapshots.Count >= MaxSnapshots)
                throw new InvalidOperationException("Too many simultaneous directory queries.");
            token = Guid.NewGuid().ToString("N");
            snapshot = new Snapshot(path, documents, now.AddMinutes(2));
            _snapshots[token] = snapshot;
        }
        else if (!_snapshots.TryGetValue(token, out snapshot!) || snapshot.Path != path)
            throw new InvalidOperationException("Directory snapshot expired or belongs to another directory.");

        var page = AgnosiaFileShuttlePayloadPager.CreatePage(snapshot.Documents, offset, maxItems, maxBytes);
        if (!page.HasMore) _snapshots.Remove(token);
        else _snapshots[token] = snapshot with { ExpiresAt = now.AddMinutes(2) };
        return page with { PageToken = token };
    }

    private sealed record Snapshot(string Path, IReadOnlyList<AgnosiaFileShuttleDocumentInfo> Documents, DateTimeOffset ExpiresAt);
}
