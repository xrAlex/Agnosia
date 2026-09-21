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
        var initial = await QueryPagesAsync(commandRunner, showAll, null, cancellationToken)
            .ConfigureAwait(false);
        if (initial.Apps is not null
            || initial.ErrorCode != AndroidCommandContract.ErrorAppInventoryUnavailable)
            return initial.Apps;

        var activity = commandRunner.CurrentActivity;
        var appContext = activity.ApplicationContext ?? activity;
        var candidates = await Task.Run(
                () => AndroidWorkProfileAppCandidates.Collect(appContext, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        if (candidates.Length == 0) return null;

        Log.Info(LogTag,
            $"Retrying work apps with Island-style package lookup. candidates={candidates.Length}, showAll={showAll}.");
        var fallback = await QueryPagesAsync(commandRunner, showAll, candidates, cancellationToken)
            .ConfigureAwait(false);
        return fallback.Apps;
    }

    private static async Task<QueryAttempt> QueryPagesAsync(
        AndroidActivityCommandGateway commandRunner,
        bool showAll,
        string[]? knownPackageNames,
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
                QueryAppsMaxJsonBytes,
                knownPackageNames);
            var envelope = new AndroidCommandEnvelope(
                Guid.NewGuid(),
                AndroidCommandKind.QueryApps,
                AndroidCommandTargetProfile.Work,
                AndroidCommandInteractivity.NonInteractive,
                AndroidCommandPriority.Refresh,
                TimeSpan.FromSeconds(30),
                JsonSerializer.Serialize(request));
            var result = await ServiceRegistry.GetRequiredService<AndroidCommandCenter>()
                .ExecuteAsync(envelope, cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                Log.Warn(LogTag, $"Failed to query work apps page {pageIndex} through command center. errorCode={result.ErrorCode}, message={result.Message}, diagnostics={result.Diagnostics}");
                return new QueryAttempt(null, pageIndex == 0 ? result.ErrorCode : null);
            }

            var response = DeserializePayload<QueryAppsResponse>(result.PayloadJson, $"work apps page {pageIndex}");
            if (response is null) return new QueryAttempt(null, null);

            var pageApps = AndroidProfileCommandJson.DeserializeAppServiceModelsResult(
                response.AppsJson,
                $"work apps page {pageIndex}");
            if (pageApps is null) return new QueryAttempt(null, null);
            apps.AddRange(pageApps);

            if (offset == 0)
                interactionPackages = response.InteractionPackages ?? [];

            var hasMore = response.HasMore;
            var nextOffset = response.NextOffset;
            if (!hasMore)
            {
                AndroidKnownWorkPackages.RememberAll(apps.Select(static app => app.PackageName));
                Log.Info(LogTag, $"Work apps query completed. count={apps.Count}, showAll={showAll}, pages={pageIndex + 1}, source={(knownPackageNames is null ? "bulk" : "known-packages")}.");
                return new QueryAttempt(new ProfileAppsQueryResult(apps, interactionPackages), null);
            }

            if (nextOffset <= offset)
            {
                Log.Warn(
                    LogTag,
                    $"Work apps paging stopped because next offset did not advance. page={pageIndex}, offset={offset}, nextOffset={nextOffset}, pageCount={pageApps.Count}.");
                return new QueryAttempt(null, null);
            }

            offset = nextOffset;
        }

        Log.Warn(
            LogTag,
            $"Work apps paging stopped after reaching the page limit. pages={QueryAppsMaxPages}, loadedApps={apps.Count}.");
        return new QueryAttempt(null, null);
    }

    private sealed record QueryAttempt(ProfileAppsQueryResult? Apps, string? ErrorCode);

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
