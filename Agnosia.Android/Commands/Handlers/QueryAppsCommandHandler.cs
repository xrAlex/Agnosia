using System.Text.Json;
using System.Text.Json.Serialization;
using Agnosia.Android.Api.Commands;
#if AGNOSIA_ANDROID
using System.Diagnostics;
using Android.Content;
using Android.Content.PM;
#endif

namespace Agnosia.Android.Commands.Handlers;

internal sealed class QueryAppsCommandHandler : IAndroidCommandHandler
{
    public AndroidCommandKind Kind => AndroidCommandKind.QueryApps;

#if AGNOSIA_ANDROID
    public async Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope,
        AndroidCommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var request = DeserializeRequest(envelope.PayloadJson);
        if (context.Context.PackageManager is not { } packageManager)
            return Failure(envelope, context, stopwatch, "PackageManager is unavailable.", "package_manager_unavailable");

        var isRiskEngineEnabled = ServiceRegistry.GetRequiredService<LocalStorageManager>()
            .GetBoolean(StorageKeys.RiskEngineEnabled, true);
        var isProfileOwner = IsProfileOwner(context);
        var admin = isProfileOwner ? context.Admin : null;
        var inventory = await GetOrCreateCachedAppInventoryQueryAsync(
                context,
                request.ShowAll,
                isRiskEngineEnabled,
                request.PageToken,
                packageManager,
                admin,
                cancellationToken)
            .ConfigureAwait(false);
        var response = CreateResponse(request, inventory);
        var payloadJson = JsonSerializer.Serialize(response);

        stopwatch.Stop();
        return AndroidCommandResultEnvelope.Success(
            envelope.CorrelationId,
            envelope.Kind,
            context.Transport,
            payloadJson,
            "App query completed.",
            stopwatch.Elapsed,
            $"appCount={inventory.Apps.Count}; responseHasMore={response.HasMore}; nextOffset={response.NextOffset}; actualProfile={context.ActualProfile}; contextSource={context.ContextSource}");
    }

    private static QueryAppsRequest DeserializeRequest(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return QueryAppsRequest.Empty;

        try
        {
            return JsonSerializer.Deserialize<QueryAppsRequest>(payloadJson) ?? QueryAppsRequest.Empty;
        }
        catch (JsonException)
        {
            return QueryAppsRequest.Empty;
        }
    }

    private static async Task<AndroidQueryCache.AppInventoryQuery> GetOrCreateCachedAppInventoryQueryAsync(
        AndroidCommandExecutionContext context,
        bool showAll,
        bool isRiskEngineEnabled,
        string? pageToken,
        PackageManager packageManager,
        ComponentName? admin,
        CancellationToken cancellationToken)
    {
        if (AndroidQueryCache.Shared.TryGetAppInventoryQuery(
                pageToken,
                showAll,
                isRiskEngineEnabled,
                out var cachedQuery))
            return cachedQuery;

        var models = await Task.Run(() => AndroidAppInventoryApi.QueryInstalledApps(
            context.Context,
            packageManager,
            context.PolicyManager,
            admin,
            showAll,
            cancellationToken,
            AppInventoryQueryOptions.WorkList), cancellationToken).ConfigureAwait(false);

        var interactionPackages = admin is not null && context.PolicyManager is not null
            ? AndroidPolicyApi.GetCrossProfilePackages(context.PolicyManager, admin)
            : [];
        var query = new AndroidQueryCache.AppInventoryQuery(
            showAll,
            isRiskEngineEnabled,
            models,
            interactionPackages);
        AndroidQueryCache.Shared.StoreAppInventoryQuery(pageToken, query);
        return query;
    }

    private static QueryAppsResponse CreateResponse(
        QueryAppsRequest request,
        AndroidQueryCache.AppInventoryQuery inventory)
    {
        if (!request.IsPaged)
        {
            return new QueryAppsResponse(
                JsonSerializer.Serialize(inventory.Apps.ToList(), AndroidApiJsonContext.Default.ListAppServiceModel),
                inventory.InteractionPackages,
                inventory.Apps.Count,
                false,
                inventory.Apps.Count);
        }

        var page = AppInventoryPayloadPager.CreatePage(
            inventory.Apps,
            request.Offset,
            request.Limit,
            request.MaxJsonBytes);

        return new QueryAppsResponse(
            page.Json,
            page.Offset == 0 ? inventory.InteractionPackages : [],
            page.NextOffset,
            page.HasMore,
            page.TotalCount);
    }

    private static bool IsProfileOwner(AndroidCommandExecutionContext context)
    {
        var packageName = context.Context.PackageName;
        return !string.IsNullOrWhiteSpace(packageName)
               && context.PolicyManager?.IsProfileOwnerApp(packageName) == true;
    }

    private static AndroidCommandResultEnvelope Failure(
        AndroidCommandEnvelope envelope,
        AndroidCommandExecutionContext context,
        Stopwatch stopwatch,
        string message,
        string errorCode)
    {
        stopwatch.Stop();
        return AndroidCommandResultEnvelope.Failure(
            envelope.CorrelationId,
            envelope.Kind,
            context.Transport,
            message,
            errorCode,
            stopwatch.Elapsed,
            string.Empty);
    }
#endif
}

internal sealed record QueryAppsRequest(
    [property: JsonPropertyName(AndroidCommandContract.ExtraShowAll)]
    bool ShowAll,
    [property: JsonPropertyName(AndroidCommandContract.ExtraQueryPageToken)]
    string? PageToken,
    [property: JsonPropertyName(AndroidCommandContract.ExtraQueryOffset)]
    int Offset,
    [property: JsonPropertyName(AndroidCommandContract.ExtraQueryLimit)]
    int Limit,
    [property: JsonPropertyName(AndroidCommandContract.ExtraQueryMaxJsonBytes)]
    int MaxJsonBytes)
{
    public static QueryAppsRequest Empty { get; } = new(false, null, 0, 0, 0);

    public bool IsPaged =>
        !string.IsNullOrWhiteSpace(PageToken)
        || Offset != 0
        || Limit != 0
        || MaxJsonBytes != 0;
}

internal sealed record QueryAppsResponse(
    [property: JsonPropertyName(AndroidCommandContract.ResultAppsJson)]
    string AppsJson,
    [property: JsonPropertyName(AndroidCommandContract.ResultInteractionPackages)]
    string[] InteractionPackages,
    [property: JsonPropertyName(AndroidCommandContract.ResultNextQueryOffset)]
    int NextOffset,
    [property: JsonPropertyName(AndroidCommandContract.ResultQueryHasMore)]
    bool HasMore,
    [property: JsonPropertyName(AndroidCommandContract.ResultQueryTotalCount)]
    int TotalCount);
