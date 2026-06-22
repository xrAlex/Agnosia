namespace Agnosia.Android.Commands;

internal interface IAndroidCommandTransport
{
    AndroidCommandTransportKind Kind { get; }

    Task<AndroidCommandResultEnvelope> ExecuteAsync(
        AndroidCommandEnvelope envelope,
        CancellationToken cancellationToken);
}
