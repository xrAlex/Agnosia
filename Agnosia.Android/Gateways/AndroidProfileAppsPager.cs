using System.Text.Json;
using Agnosia.Android.Commands.Handlers;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Gateways;

internal static class AndroidProfileAppsPager
{
    private const string LogTag = "AgnosiaProfileCommand";
    private const int QueryAppsPageLimit = 100;
    private const int QueryAppsMaxJsonBytes = 512 * 1024;
    private const int QueryAppsMaxPages = 100;

    public static async Task<ProfileAppsQueryResult?> QueryWorkAppsPagedAsync(
        AndroidActivityCommandGateway commandRunner,
        bool showAll,
        CancellationToken cancellationToken)
    {
        var apps = new List<AppServiceModel>();
        IReadOnlyList<string> interactionPackages = [];
        var pageToken = Guid.NewGuid().ToString("N");
        var offset = 0;

        for (var pageIndex = 0; pageIndex < QueryAppsMaxPages; pageIndex++)
        {
            var request = new QueryAppsRequest(
                showAll,
                pageToken,
                offset,
                QueryAppsPageLimit,
                QueryAppsMaxJsonBytes);
            var envelope = new AndroidCommandEnvelope(
                Guid.NewGuid(),
                AndroidCommandKind.QueryApps,
                AndroidCommandTargetProfile.Work,
                AndroidCommandInteractivity.Silent,
                AndroidCommandPriority.Refresh,
                TimeSpan.FromSeconds(30),
                JsonSerializer.Serialize(request));
            var result = await ServiceRegistry.GetRequiredService<AndroidCommandCenter>()
                .ExecuteAsync(envelope, cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                Log.Warn(LogTag, $"Failed to query work apps page {pageIndex} through command center. diagnostics={result.Diagnostics}");
                return null;
            }

            var response = DeserializePayload<QueryAppsResponse>(result.PayloadJson, $"work apps page {pageIndex}");
            if (response is null) return null;

            var pageApps = AndroidProfileCommandJson.DeserializeAppServiceModelsResult(
                response.AppsJson,
                $"work apps page {pageIndex}") ?? [];
            apps.AddRange(pageApps);

            if (offset == 0)
                interactionPackages = response.InteractionPackages ?? [];

            var hasMore = response.HasMore;
            var nextOffset = response.NextOffset;
            if (!hasMore) return new ProfileAppsQueryResult(apps, interactionPackages);

            if (nextOffset <= offset)
            {
                Log.Warn(
                    LogTag,
                    $"Work apps paging stopped because next offset did not advance. page={pageIndex}, offset={offset}, nextOffset={nextOffset}, pageCount={pageApps.Count}.");
                return null;
            }

            offset = nextOffset;
        }

        Log.Warn(
            LogTag,
            $"Work apps paging stopped after reaching the page limit. pages={QueryAppsMaxPages}, loadedApps={apps.Count}.");
        return null;
    }

    private static T? DeserializePayload<T>(string? payloadJson, string description)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return default;

        try
        {
            return JsonSerializer.Deserialize<T>(payloadJson);
        }
        catch (JsonException exception)
        {
            Log.Warn(LogTag, $"Failed to deserialize {description}: {exception.Message}");
            return default;
        }
    }
}
