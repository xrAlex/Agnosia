namespace Agnosia.Android.Commands;

internal static class CommandResultEnvelopeIdentity
{
    public static CommandResultEnvelopeIdentityValidation Validate(
        AndroidCommandEnvelope request,
        AndroidCommandResultEnvelope response)
    {
        if (response.CorrelationId != request.CorrelationId)
            return CommandResultEnvelopeIdentityValidation.Failure("command_result_correlation_mismatch");

        return response.Kind == request.Kind
            ? CommandResultEnvelopeIdentityValidation.Success
            : CommandResultEnvelopeIdentityValidation.Failure("command_result_kind_mismatch");
    }
}

internal sealed record CommandResultEnvelopeIdentityValidation(bool Succeeded, string? ErrorCode)
{
    public static CommandResultEnvelopeIdentityValidation Success { get; } = new(true, null);

    public static CommandResultEnvelopeIdentityValidation Failure(string errorCode)
    {
        return new CommandResultEnvelopeIdentityValidation(false, errorCode);
    }
}
