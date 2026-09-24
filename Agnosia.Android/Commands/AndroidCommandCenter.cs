#if AGNOSIA_ANDROID
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;
#endif
using Agnosia.Models;

namespace Agnosia.Android.Commands;

internal sealed class AndroidCommandCenter
{
    private const string LogTag = "AgnosiaCommandCenter";

    private readonly AndroidCommandScheduler _scheduler;
    private readonly IReadOnlyDictionary<AndroidCommandTransportKind, IAndroidCommandTransport> _transports;
    private readonly Func<bool> _providerEnabled;
    private readonly Func<CommandTransportPreference> _transportPreference;

    public AndroidCommandCenter(
        AndroidCommandScheduler scheduler,
        IEnumerable<IAndroidCommandTransport> transports,
        Func<bool>? providerEnabled = null,
        Func<CommandTransportPreference>? transportPreference = null)
    {
        _scheduler = scheduler;
        _transports = transports.ToDictionary(transport => transport.Kind);
        _providerEnabled = providerEnabled ?? (() => false);
        _transportPreference = transportPreference ?? (() => CommandTransportPreference.Auto);
    }

    public async Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var preference = _transportPreference();
        var providerEnabled = preference == CommandTransportPreference.Auto && _providerEnabled();
        var route = AndroidCommandRouter.GetRoute(envelope, providerEnabled, preference);
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeout = envelope.Timeout > TimeSpan.Zero
            ? envelope.Timeout
            : TimeSpan.FromSeconds(30);
        timeoutCancellation.CancelAfter(timeout);

        try
        {
            return await _scheduler.RunAsync(
                    envelope,
                    token => ExecuteWithFallbackAsync(envelope, route, token),
                    timeoutCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                                                && timeoutCancellation.IsCancellationRequested)
        {
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
        AndroidCommandRoute route,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        AndroidCommandResultEnvelope? lastFailure = null;

        for (var index = 0; index < route.Transports.Count; index++)
        {
            var transportKind = route.Transports[index];
            if (!_transports.TryGetValue(transportKind, out var transport))
            {
                diagnostics.Add($"missing={transportKind}");
                continue;
            }

            AndroidCommandResultEnvelope result;
            using var attemptCancellation = transportKind == AndroidCommandTransportKind.Provider
                                            && index < route.Transports.Count - 1
                                            && !ProviderCommandPolicy.IsMutation(envelope.Kind)
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : null;
            if (attemptCancellation is not null)
            {
                var totalMilliseconds = envelope.Timeout > TimeSpan.Zero
                    ? envelope.Timeout.TotalMilliseconds
                    : TimeSpan.FromSeconds(30).TotalMilliseconds;
                attemptCancellation.CancelAfter(TimeSpan.FromMilliseconds(
                    Math.Clamp(totalMilliseconds / 3, 250, 5_000)));
            }
            try
            {
                result = await transport.ExecuteAsync(envelope, attemptCancellation?.Token ?? cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (attemptCancellation?.IsCancellationRequested == true
                                                    && !cancellationToken.IsCancellationRequested)
            {
                result = AndroidCommandResultEnvelope.Failure(
                    envelope.CorrelationId,
                    envelope.Kind,
                    transportKind,
                    "Provider did not respond in time.",
                    "provider_timeout",
                    TimeSpan.Zero,
                    "provider attempt timed out before Activity fallback");
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

            if (index == route.Transports.Count - 1
                || transportKind == AndroidCommandTransportKind.Provider
                   && !ProviderCommandPolicy.CanFallbackToActivity(envelope.Kind, result.ErrorCode))
            {
                diagnostics.Add($"terminalFailure={transportKind}; reason={result.ErrorCode ?? "failed"}");
                return Complete(envelope, result, diagnostics);
            }

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

}
