using Agnosia.Android.Commands;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class CommandResultEnvelopeIdentityTests
{
    private static readonly Guid CorrelationId = Guid.Parse("fc70f664-f53c-4e77-80f9-cd2f9141dc65");

    [Fact]
    public void Validate_accepts_matching_correlation_and_kind()
    {
        var validation = CommandResultEnvelopeIdentity.Validate(
            CreateRequest(),
            CreateResponse(CorrelationId, AndroidCommandKind.QueryLogs));

        Assert.True(validation.Succeeded);
        Assert.Null(validation.ErrorCode);
    }

    [Fact]
    public void Validate_rejects_mismatched_correlation()
    {
        var validation = CommandResultEnvelopeIdentity.Validate(
            CreateRequest(),
            CreateResponse(Guid.Parse("6efb654c-7552-4d60-87d3-d1c5d4fa74da"), AndroidCommandKind.QueryLogs));

        Assert.False(validation.Succeeded);
        Assert.Equal("command_result_correlation_mismatch", validation.ErrorCode);
    }

    [Fact]
    public void Validate_rejects_mismatched_kind()
    {
        var validation = CommandResultEnvelopeIdentity.Validate(
            CreateRequest(),
            CreateResponse(CorrelationId, AndroidCommandKind.ProfilePing));

        Assert.False(validation.Succeeded);
        Assert.Equal("command_result_kind_mismatch", validation.ErrorCode);
    }

    private static AndroidCommandEnvelope CreateRequest()
    {
        return new AndroidCommandEnvelope(
            CorrelationId,
            AndroidCommandKind.QueryLogs,
            AndroidCommandTargetProfile.Work,
            AndroidCommandInteractivity.NonInteractive,
            AndroidCommandPriority.Mutation,
            TimeSpan.FromSeconds(30),
            "{}");
    }

    private static AndroidCommandResultEnvelope CreateResponse(Guid correlationId, AndroidCommandKind kind)
    {
        return AndroidCommandResultEnvelope.Success(
            correlationId,
            kind,
            AndroidCommandTransportKind.Activity,
            null,
            "handled",
            TimeSpan.Zero,
            string.Empty);
    }
}
