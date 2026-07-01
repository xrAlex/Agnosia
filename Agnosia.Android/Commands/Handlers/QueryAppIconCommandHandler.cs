using System.Text.Json;
using System.Text.Json.Serialization;
using Agnosia.Android.Api.Commands;
#if AGNOSIA_ANDROID
using System.Diagnostics;
#endif

namespace Agnosia.Android.Commands.Handlers;

internal sealed class QueryAppIconCommandHandler : IAndroidCommandHandler
{
    public AndroidCommandKind Kind => AndroidCommandKind.QueryAppIcon;

#if AGNOSIA_ANDROID
    public async Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope,
        AndroidCommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var request = DeserializeRequest(envelope.PayloadJson);
        if (string.IsNullOrWhiteSpace(request.PackageName))
            return Success(envelope, context, stopwatch, null);

        if (context.Context.PackageManager is not { } packageManager)
            return Failure(envelope, context, stopwatch, "PackageManager is unavailable.", "package_manager_unavailable");

        var iconPng = await Task.Run(
                () => AndroidAppIconWarmupQueue.TryLoadCachedOrQueue(
                    context.Context,
                    packageManager,
                    request.PackageName),
                cancellationToken)
            .ConfigureAwait(false);

        return Success(envelope, context, stopwatch, iconPng);
    }

    private static QueryAppIconRequest DeserializeRequest(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return QueryAppIconRequest.Empty;

        try
        {
            return JsonSerializer.Deserialize<QueryAppIconRequest>(payloadJson) ?? QueryAppIconRequest.Empty;
        }
        catch (JsonException)
        {
            return QueryAppIconRequest.Empty;
        }
    }

    private static AndroidCommandResultEnvelope Success(
        AndroidCommandEnvelope envelope,
        AndroidCommandExecutionContext context,
        Stopwatch stopwatch,
        byte[]? iconPng)
    {
        var payloadJson = JsonSerializer.Serialize(new QueryAppIconResponse(iconPng));

        stopwatch.Stop();
        return AndroidCommandResultEnvelope.Success(
            envelope.CorrelationId,
            envelope.Kind,
            context.Transport,
            payloadJson,
            "App icon query completed.",
            stopwatch.Elapsed,
            $"iconBytes={iconPng?.Length ?? 0}");
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

internal sealed record QueryAppIconRequest(
    [property: JsonPropertyName(AndroidCommandContract.ExtraPackage)]
    string? PackageName)
{
    public static QueryAppIconRequest Empty { get; } = new(PackageName: null);
}

internal sealed record QueryAppIconResponse(
    [property: JsonPropertyName(AndroidCommandContract.ResultIconPng)]
    byte[]? IconPng);
