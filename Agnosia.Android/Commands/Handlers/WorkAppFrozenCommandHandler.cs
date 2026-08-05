#if AGNOSIA_ANDROID
using System.Diagnostics;
using Agnosia.Android.Vpn;
#endif

namespace Agnosia.Android.Commands.Handlers;

internal sealed class WorkAppFrozenCommandHandler : IAndroidCommandHandler
{
    private const string LogTag = "AgnosiaWorkFrozenCommand";

    public AndroidCommandKind Kind => AndroidCommandKind.WorkAppFrozen;

#if AGNOSIA_ANDROID
    public async Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope,
        AndroidCommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        if (!WorkAppFrozenCommandPayload.TryDeserialize(envelope.PayloadJson, out var payload))
            return Failure(
                envelope,
                context,
                stopwatch,
                "Work-app frozen command payload is invalid.",
                "work_app_frozen_payload_invalid");

        var result = await WorkAppFrozenHandler.RestoreParentVpnAndHideOverlayAsync(
                context.Context,
                payload.PackageName,
                payload.LaunchId,
                payload.Trigger,
                LogTag,
                cancellationToken)
            .ConfigureAwait(false);
        stopwatch.Stop();
        return result.Succeeded
            ? AndroidCommandResultEnvelope.Success(
                envelope.CorrelationId,
                envelope.Kind,
                context.Transport,
                null,
                result.Message,
                stopwatch.Elapsed,
                $"package={payload.PackageName}; launchId={payload.LaunchId}")
            : AndroidCommandResultEnvelope.Failure(
                envelope.CorrelationId,
                envelope.Kind,
                context.Transport,
                result.Message,
                "work_app_frozen_restore_failed",
                stopwatch.Elapsed,
                $"package={payload.PackageName}; launchId={payload.LaunchId}");
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
            $"actual={context.ActualProfile}; contextSource={context.ContextSource}");
    }
#endif
}
