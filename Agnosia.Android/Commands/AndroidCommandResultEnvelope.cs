namespace Agnosia.Android.Commands;

internal sealed record AndroidCommandResultEnvelope(
    Guid CorrelationId,
    AndroidCommandKind Kind,
    bool Succeeded,
    AndroidCommandTransportKind Transport,
    string? PayloadJson,
    string Message,
    string? ErrorCode,
    TimeSpan Elapsed,
    string Diagnostics)
{
    public static AndroidCommandResultEnvelope Success(
        Guid correlationId,
        AndroidCommandKind kind,
        AndroidCommandTransportKind transport,
        string? payloadJson,
        string message,
        TimeSpan elapsed,
        string diagnostics)
    {
        return new AndroidCommandResultEnvelope(
            correlationId,
            kind,
            true,
            transport,
            payloadJson,
            message,
            null,
            elapsed,
            diagnostics);
    }

    public static AndroidCommandResultEnvelope Failure(
        Guid correlationId,
        AndroidCommandKind kind,
        AndroidCommandTransportKind transport,
        string message,
        string errorCode,
        TimeSpan elapsed,
        string diagnostics)
    {
        return new AndroidCommandResultEnvelope(
            correlationId,
            kind,
            false,
            transport,
            null,
            message,
            errorCode,
            elapsed,
            diagnostics);
    }
}
