using Agnosia.Android.Commands;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class ActivityCommandResultIdentityTests
{
    private static readonly Guid CorrelationId = Guid.Parse("5b0d3568-952f-4fbe-a246-e7357896c462");

    [Fact]
    public void Validate_AcceptsMatchingIdentity()
    {
        var result = ActivityCommandResultIdentity.Validate(
            CorrelationId,
            AndroidCommandKind.InstallPackage,
            -1,
            CorrelationId.ToString("D"),
            nameof(AndroidCommandKind.InstallPackage),
            -1);

        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("c3414701-fee2-4bdd-9970-ea9a783129df")]
    public void Validate_RejectsMissingMalformedOrMismatchedCorrelation(string? actualCorrelationId)
    {
        var result = ActivityCommandResultIdentity.Validate(
            CorrelationId,
            AndroidCommandKind.InstallPackage,
            -1,
            actualCorrelationId,
            nameof(AndroidCommandKind.InstallPackage),
            -1);

        Assert.False(result.Succeeded);
        Assert.Equal("activity_result_correlation_mismatch", result.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Unknown")]
    [InlineData(nameof(AndroidCommandKind.UninstallPackage))]
    public void Validate_RejectsMissingMalformedOrMismatchedKind(string? actualKind)
    {
        var result = ActivityCommandResultIdentity.Validate(
            CorrelationId,
            AndroidCommandKind.InstallPackage,
            -1,
            CorrelationId.ToString("D"),
            actualKind,
            -1);

        Assert.False(result.Succeeded);
        Assert.Equal("activity_result_kind_mismatch", result.ErrorCode);
    }

    [Fact]
    public void Validate_RejectsMismatchedResultCode()
    {
        var result = ActivityCommandResultIdentity.Validate(
            CorrelationId,
            AndroidCommandKind.InstallPackage,
            -1,
            CorrelationId.ToString("D"),
            nameof(AndroidCommandKind.InstallPackage),
            0);

        Assert.False(result.Succeeded);
        Assert.Equal("activity_result_code_mismatch", result.ErrorCode);
    }
}
