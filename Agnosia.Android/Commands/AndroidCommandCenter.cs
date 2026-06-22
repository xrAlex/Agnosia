#if AGNOSIA_ANDROID
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;
#endif

namespace Agnosia.Android.Commands;

internal sealed class AndroidCommandCenter
{
    private const string LogTag = "AgnosiaCommandCenter";

    private readonly AndroidCommandScheduler _scheduler;
    private readonly IReadOnlyDictionary<AndroidCommandTransportKind, IAndroidCommandTransport> _transports;

    public AndroidCommandCenter(
        AndroidCommandScheduler scheduler,
        IEnumerable<IAndroidCommandTransport> transports)
    {
        _scheduler = scheduler;
        _transports = transports.ToDictionary(transport => transport.Kind);
    }

    public async Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope,
        CancellationToken cancellationToken)
    {
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeout = envelope.Timeout > TimeSpan.Zero
            ? envelope.Timeout
            : TimeSpan.FromSeconds(30);
        timeoutCancellation.CancelAfter(timeout);

        try
        {
            return await _scheduler.RunAsync(
                    envelope,
                    token => ExecuteWithFallbackAsync(envelope, token),
                    timeoutCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                                                && timeoutCancellation.IsCancellationRequested)
        {
            var route = AndroidCommandRouter.GetRoute(envelope);
            var timeoutTransport = route.Transports.FirstOrDefault();
            return AndroidCommandResultEnvelope.Failure(
                envelope.CorrelationId,
                envelope.Kind,
                timeoutTransport,
                "Android command timed out.",
                "command_timeout",
                timeout,
                $"timeoutMs={timeout.TotalMilliseconds:0}");
        }
    }

    private async Task<AndroidCommandResultEnvelope> ExecuteWithFallbackAsync(
        AndroidCommandEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        var route = AndroidCommandRouter.GetRoute(envelope);
        AndroidCommandResultEnvelope? lastFailure = null;

        foreach (var transportKind in route.Transports)
        {
            if (!_transports.TryGetValue(transportKind, out var transport))
            {
                diagnostics.Add($"missing={transportKind}");
                continue;
            }

            AndroidCommandResultEnvelope result;
            try
            {
                result = await transport.ExecuteAsync(envelope, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                result = AndroidCommandResultEnvelope.Failure(
                    envelope.CorrelationId,
                    envelope.Kind,
                    transportKind,
                    $"Command transport {transportKind} failed.",
                    "transport_exception",
                    TimeSpan.Zero,
                    exception.ToString());
            }

            if (result.Succeeded)
                return Complete(envelope, result, diagnostics);

            lastFailure = result;
            diagnostics.Add($"fallbackFrom={transportKind}; reason={result.ErrorCode ?? "failed"}");
        }

        if (lastFailure is not null)
            return Complete(envelope, lastFailure, diagnostics);

        var fallbackTransport = route.Transports.LastOrDefault();
        return Complete(envelope, AndroidCommandResultEnvelope.Failure(
            envelope.CorrelationId,
            envelope.Kind,
            fallbackTransport,
            "No command transport is available.",
            "transport_missing",
            TimeSpan.Zero,
            string.Join("; ", diagnostics)), diagnostics);
    }

    private static AndroidCommandResultEnvelope Complete(
        AndroidCommandEnvelope envelope,
        AndroidCommandResultEnvelope result,
        IReadOnlyCollection<string> diagnostics)
    {
        var completed = AppendDiagnostics(result, diagnostics);
        LogCompletion(envelope, completed, diagnostics);
        LogSilentFallbackIfNeeded(envelope, completed, diagnostics);
        return completed;
    }

    private static AndroidCommandResultEnvelope AppendDiagnostics(
        AndroidCommandResultEnvelope result,
        IReadOnlyCollection<string> diagnostics)
    {
        if (diagnostics.Count == 0)
            return result;

        var fallbackDiagnostics = string.Join("; ", diagnostics);
        if (string.IsNullOrWhiteSpace(result.Diagnostics))
            return result with { Diagnostics = fallbackDiagnostics };

        return result with { Diagnostics = $"{result.Diagnostics}; {fallbackDiagnostics}" };
    }

    private static void LogCompletion(
        AndroidCommandEnvelope envelope,
        AndroidCommandResultEnvelope result,
        IReadOnlyCollection<string> diagnostics)
    {
        LogDebug(
            LogTag,
            $"Command completed. correlationId={envelope.CorrelationId}; kind={envelope.Kind}; priority={envelope.Priority}; interactivity={envelope.Interactivity}; targetProfile={envelope.TargetProfile}; transport={result.Transport}; fallbackFrom={FormatFallbacks(diagnostics)}; elapsedMs={result.Elapsed.TotalMilliseconds:0}; succeeded={result.Succeeded}; errorCode={result.ErrorCode ?? "<none>"}");
    }

    private static void LogSilentFallbackIfNeeded(
        AndroidCommandEnvelope envelope,
        AndroidCommandResultEnvelope result,
        IReadOnlyCollection<string> diagnostics)
    {
        if (result.Transport != AndroidCommandTransportKind.Activity
            || !diagnostics.Any(static item => item.StartsWith("fallbackFrom=SilentWorkProfile", StringComparison.Ordinal)))
            return;

        LogWarn(
            LogTag,
            $"Silent command fell back to Activity. correlationId={envelope.CorrelationId}; kind={envelope.Kind}; targetProfile={envelope.TargetProfile}; diagnostics={string.Join(";", diagnostics)}");
    }

    private static string FormatFallbacks(IReadOnlyCollection<string> diagnostics)
    {
        if (diagnostics.Count == 0) return "<none>";

        var fallbacks = diagnostics
            .Where(static item => item.StartsWith("fallbackFrom=", StringComparison.Ordinal))
            .Select(static item => item.Split(';', 2)[0]["fallbackFrom=".Length..])
            .ToArray();
        return fallbacks.Length == 0 ? "<none>" : string.Join(",", fallbacks);
    }

    private static void LogDebug(string tag, string message)
    {
#if AGNOSIA_ANDROID
        Log.Debug(tag, message);
#else
        _ = tag;
        _ = message;
#endif
    }

    private static void LogWarn(string tag, string message)
    {
#if AGNOSIA_ANDROID
        Log.Warn(tag, message);
#else
        _ = tag;
        _ = message;
#endif
    }
}
