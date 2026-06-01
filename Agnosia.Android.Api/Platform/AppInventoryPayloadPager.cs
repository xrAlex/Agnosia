using System.Text;
using System.Text.Json;
using Agnosia.Android.Api.Serialization;

namespace Agnosia.Android.Api.Platform;

public static class AppInventoryPayloadPager
{
    public static AppInventoryPayloadPage CreatePage(
        IReadOnlyList<AppServiceModel> apps,
        int offset,
        int limit,
        int maxJsonBytes)
    {
        ArgumentNullException.ThrowIfNull(apps);

        var totalCount = apps.Count;
        var safeOffset = Math.Clamp(offset, 0, totalCount);
        var remainingCount = totalCount - safeOffset;
        if (remainingCount == 0)
            return CreateResult([], safeOffset, safeOffset, false, totalCount);

        var maxItems = limit <= 0
            ? remainingCount
            : Math.Min(limit, remainingCount);
        var pageItems = new List<AppServiceModel>(maxItems);
        var json = "[]";
        var jsonBytes = Encoding.UTF8.GetByteCount(json);
        var effectiveMaxJsonBytes = maxJsonBytes <= 0 ? int.MaxValue : maxJsonBytes;

        for (var index = 0; index < maxItems; index++)
        {
            pageItems.Add(apps[safeOffset + index]);
            var candidateJson = JsonSerializer.Serialize(pageItems, AndroidApiJsonContext.Default.ListAppServiceModel);
            var candidateBytes = Encoding.UTF8.GetByteCount(candidateJson);

            if (candidateBytes > effectiveMaxJsonBytes && pageItems.Count > 1)
            {
                pageItems.RemoveAt(pageItems.Count - 1);
                break;
            }

            json = candidateJson;
            jsonBytes = candidateBytes;

            if (candidateBytes > effectiveMaxJsonBytes)
                break;
        }

        if (pageItems.Count == 0)
        {
            pageItems.Add(apps[safeOffset]);
            json = JsonSerializer.Serialize(pageItems, AndroidApiJsonContext.Default.ListAppServiceModel);
            jsonBytes = Encoding.UTF8.GetByteCount(json);
        }

        var nextOffset = safeOffset + pageItems.Count;
        return new AppInventoryPayloadPage(
            pageItems,
            json,
            safeOffset,
            nextOffset,
            nextOffset < totalCount,
            totalCount,
            jsonBytes);
    }

    private static AppInventoryPayloadPage CreateResult(
        IReadOnlyList<AppServiceModel> apps,
        int offset,
        int nextOffset,
        bool hasMore,
        int totalCount)
    {
        var json = JsonSerializer.Serialize(apps.ToList(), AndroidApiJsonContext.Default.ListAppServiceModel);
        return new AppInventoryPayloadPage(
            apps,
            json,
            offset,
            nextOffset,
            hasMore,
            totalCount,
            Encoding.UTF8.GetByteCount(json));
    }
}

public sealed record AppInventoryPayloadPage(
    IReadOnlyList<AppServiceModel> Apps,
    string Json,
    int Offset,
    int NextOffset,
    bool HasMore,
    int TotalCount,
    int JsonUtf8Bytes);
