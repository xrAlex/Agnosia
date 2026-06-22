#if AGNOSIA_ANDROID
using System.Diagnostics;
using Agnosia.Android.Gateways;

namespace Agnosia.Android.Commands.Transports;

internal sealed class ActivityCommandTransport(
    AndroidActivityCommandGateway gateway) : IAndroidCommandTransport
{
    public AndroidCommandTransportKind Kind => AndroidCommandTransportKind.Activity;

    public async Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var intent = AndroidCommandIntentMapper.ToIntent(envelope);
        var useWorkProfile = envelope.TargetProfile == AndroidCommandTargetProfile.Work;
        var result = await gateway.StartActivityForResultAsync(
                intent,
                useWorkProfile,
                cancellationToken)
            .ConfigureAwait(false);

        stopwatch.Stop();
        if (result.ResultCode != Result.Ok)
        {
            var error = AndroidActivityResultApi.ExtractError(result);
            return AndroidCommandResultEnvelope.Failure(
                envelope.CorrelationId,
                envelope.Kind,
                Kind,
                string.IsNullOrWhiteSpace(error)
                    ? "Android activity command was canceled."
                    : error,
                "activity_result_canceled",
                stopwatch.Elapsed,
                $"result={result.ResultCode}; action={intent.Action ?? "<none>"}");
        }

        var message = AndroidActivityResultApi.ExtractMessage(result);
        var diagnostics = AndroidCommandIntentMapper.ReadDiagnostics(result.Data);
        diagnostics = string.IsNullOrWhiteSpace(diagnostics)
            ? $"result={result.ResultCode}; action={intent.Action ?? "<none>"}"
            : $"{diagnostics}; result={result.ResultCode}; action={intent.Action ?? "<none>"}";
        return AndroidCommandResultEnvelope.Success(
            envelope.CorrelationId,
            envelope.Kind,
            Kind,
            AndroidCommandIntentMapper.ReadPayloadJson(envelope, result.Data),
            string.IsNullOrWhiteSpace(message)
                ? "Android activity command completed."
                : message,
            stopwatch.Elapsed,
            diagnostics);
    }
}
#endif
