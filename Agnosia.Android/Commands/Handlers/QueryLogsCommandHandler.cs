#if AGNOSIA_ANDROID
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
#endif

namespace Agnosia.Android.Commands.Handlers;

internal sealed class QueryLogsCommandHandler : IAndroidCommandHandler
{
    public AndroidCommandKind Kind => AndroidCommandKind.QueryLogs;

#if AGNOSIA_ANDROID
    public Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope,
        AndroidCommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
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
