using System.Text.Json;
using System.Text.Json.Serialization;
using Agnosia.Android.Api.Commands;
#if AGNOSIA_ANDROID
using System.Diagnostics;
#endif

namespace Agnosia.Android.Commands.Handlers;

internal sealed class QueryAppIconsCommandHandler : IAndroidCommandHandler
{
    public AndroidCommandKind Kind => AndroidCommandKind.QueryAppIcons;

#if AGNOSIA_ANDROID
    public async Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope,
        AndroidCommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var request = DeserializeRequest(envelope.PayloadJson);
        if (request.PackageNames.Length == 0)
            return Success(envelope, context, stopwatch, new Dictionary<string, byte[]?>(StringComparer.Ordinal));

        if (context.Context.PackageManager is not { } packageManager)
            return Failure(envelope, context, stopwatch, "PackageManager is unavailable.", "package_manager_unavailable");

        var icons = await Task.Run<IReadOnlyDictionary<string, byte[]?>>(() =>
        {
            var loadedIcons = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
            foreach (var packageName in request.PackageNames.Distinct(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                loadedIcons[packageName] = AndroidAppIconWarmupQueue.TryLoadCachedOrQueue(
                    context.Context,
                    packageManager,
                    packageName);
            }

            return loadedIcons;
        }, cancellationToken).ConfigureAwait(false);

        return Success(envelope, context, stopwatch, icons);
    }

    private static QueryAppIconsRequest DeserializeRequest(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return QueryAppIconsRequest.Empty;

        try
        {
            return JsonSerializer.Deserialize<QueryAppIconsRequest>(payloadJson) ?? QueryAppIconsRequest.Empty;
        }
        catch (JsonException)
        {
            return QueryAppIconsRequest.Empty;
        }
    }

    private static AndroidCommandResultEnvelope Success(
        AndroidCommandEnvelope envelope,
        AndroidCommandExecutionContext context,
        Stopwatch stopwatch,
        IReadOnlyDictionary<string, byte[]?> icons)
    {
        var payloadJson = JsonSerializer.Serialize(new QueryAppIconsResponse(icons));

        stopwatch.Stop();
        return AndroidCommandResultEnvelope.Success(
            envelope.CorrelationId,
            envelope.Kind,
            context.Transport,
            payloadJson,
            "App icon batch query completed.",
            stopwatch.Elapsed,
            $"iconCount={icons.Count}");
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

internal sealed record QueryAppIconsRequest(
    [property: JsonPropertyName(AndroidCommandContract.ExtraPackages)]
    string[] PackageNames)
{
    public static QueryAppIconsRequest Empty { get; } = new([]);
}

internal sealed record QueryAppIconsResponse(
    [property: JsonPropertyName(AndroidCommandContract.ResultIconsBundle)]
    IReadOnlyDictionary<string, byte[]?> Icons);
