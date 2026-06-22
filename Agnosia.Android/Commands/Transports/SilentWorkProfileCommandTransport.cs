namespace Agnosia.Android.Commands.Transports;

internal sealed class SilentWorkProfileCommandTransport : IAndroidCommandTransport
{
    public AndroidCommandTransportKind Kind => AndroidCommandTransportKind.SilentWorkProfile;

    public Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(AndroidCommandResultEnvelope.Failure(
            envelope.CorrelationId,
            envelope.Kind,
            Kind,
            "Silent work-profile command transport is not available on this Android profile topology.",
            "silent_work_transport_unavailable",
            TimeSpan.Zero,
            "capability=unsupported; fallback=activity"));
    }
}
