using System.Text;
using System.Text.Json;

namespace Agnosia.Android.Files;

internal static class AgnosiaFileShuttlePayloadPager
{
    public static AgnosiaFileShuttlePayloadPage CreatePage(
        IReadOnlyList<AgnosiaFileShuttleDocumentInfo> documents,
        int offset,
        int maxItems,
        int maxJsonBytes)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxItems);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxJsonBytes, 2);

        if (offset >= documents.Count)
            return new AgnosiaFileShuttlePayloadPage("[]", documents.Count, false, 2);

        var pageDocuments = new List<string>(
            Math.Min(maxItems, documents.Count - offset));
        var jsonBytes = 2;

        for (var index = offset; index < documents.Count && pageDocuments.Count < maxItems; index++)
        {
            var documentJson = JsonSerializer.Serialize(
                documents[index],
                AgnosiaFileShuttleJsonContext.Default.AgnosiaFileShuttleDocumentInfo);
            var candidateBytes = jsonBytes + Encoding.UTF8.GetByteCount(documentJson)
                                 + (pageDocuments.Count == 0 ? 0 : 1);
            if (candidateBytes > maxJsonBytes)
            {
                if (pageDocuments.Count == 0)
                    throw new InvalidOperationException(
                        "A File Shuttle document exceeds the configured Binder payload limit.");

                break;
            }

            pageDocuments.Add(documentJson);
            jsonBytes = candidateBytes;
        }

        var nextOffset = offset + pageDocuments.Count;
        return new AgnosiaFileShuttlePayloadPage(
            "[" + string.Join(",", pageDocuments) + "]",
            nextOffset,
            nextOffset < documents.Count,
            jsonBytes);
    }

}

internal sealed record AgnosiaFileShuttlePayloadPage(
    string Json,
    int NextOffset,
    bool HasMore,
    int JsonUtf8Bytes,
    string? PageToken = null);
