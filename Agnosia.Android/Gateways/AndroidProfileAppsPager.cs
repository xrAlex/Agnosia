using Android.Content;
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
            var intent = new Intent(AgnosiaActions.QueryApps);
            intent.PutExtra(AndroidCommandContract.ExtraShowAll, showAll);
            intent.PutExtra(AndroidCommandContract.ExtraQueryPageToken, pageToken);
            intent.PutExtra(AndroidCommandContract.ExtraQueryOffset, offset);
            intent.PutExtra(AndroidCommandContract.ExtraQueryLimit, QueryAppsPageLimit);
            intent.PutExtra(AndroidCommandContract.ExtraQueryMaxJsonBytes, QueryAppsMaxJsonBytes);

            var data = await AndroidProfileCommandTransport.StartForDataAsync(
                commandRunner,
                intent,
                $"Failed to query work apps page {pageIndex} through the profile activity command.",
                cancellationToken).ConfigureAwait(false);
            if (data is null) return null;

            var pageApps = AndroidProfileCommandJson.DeserializeAppServiceModelsResult(
                data.GetStringExtra(AndroidCommandContract.ResultAppsJson),
                $"work apps page {pageIndex}") ?? [];
            apps.AddRange(pageApps);

            if (offset == 0)
                interactionPackages =
                    data.GetStringArrayExtra(AndroidCommandContract.ResultInteractionPackages) ?? [];

            var hasMore = data.GetBooleanExtra(AndroidCommandContract.ResultQueryHasMore, false);
            var nextOffset = data.GetIntExtra(AndroidCommandContract.ResultNextQueryOffset, offset + pageApps.Count);
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
}
