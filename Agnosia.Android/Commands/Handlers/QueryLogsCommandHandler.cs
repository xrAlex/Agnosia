#if AGNOSIA_ANDROID
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
#endif

namespace Agnosia.Android.Commands.Handlers;

internal sealed class QueryLogsCommandHandler : IAndroidCommandHandler
{
    private readonly ProviderLogPageStore _pages = new();
    public AndroidCommandKind Kind => AndroidCommandKind.QueryLogs;

#if AGNOSIA_ANDROID
    public Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope,
        AndroidCommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        if (context.Transport == AndroidCommandTransportKind.Provider)
        {
            try
            {
                var request = JsonSerializer.Deserialize<ProviderLogPageRequest>(envelope.PayloadJson ?? "null")
                              ?? throw new InvalidDataException("Log page request is missing.");
                var page = _pages.Read(request, () => AndroidAppLogArchive.Load(context.Context), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                return Task.FromResult(AndroidCommandResultEnvelope.Success(envelope.CorrelationId, envelope.Kind,
                    context.Transport, JsonSerializer.Serialize(page), "Log page loaded.", stopwatch.Elapsed, "paged snapshot"));
            }
            catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
            {
                return Task.FromResult(AndroidCommandResultEnvelope.Failure(envelope.CorrelationId, envelope.Kind,
                    context.Transport, "Could not read log page.", exception is InvalidDataException ? "payload_too_large" : "snapshot_unavailable",
                    stopwatch.Elapsed, "paged snapshot"));
            }
        }
        var logs = AndroidAppLogArchive.Load(context.Context).ToList();
        var logsJson = JsonSerializer.Serialize(logs, AndroidApiJsonContext.Default.ListAppLogEntry);
        var payloadJson = JsonSerializer.Serialize(new QueryLogsPayload(logsJson));

        stopwatch.Stop();
        return Task.FromResult(AndroidCommandResultEnvelope.Success(
            envelope.CorrelationId,
            envelope.Kind,
            context.Transport,
            payloadJson,
            "Log query completed.",
            stopwatch.Elapsed,
            $"logCount={logs.Count}"));
    }

    private sealed record QueryLogsPayload(
        [property: JsonPropertyName(AndroidCommandContract.ResultLogsJson)]
        string LogsJson);
#endif
}
