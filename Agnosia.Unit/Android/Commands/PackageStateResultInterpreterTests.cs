using System.Text.Json;
using Agnosia.Android.Commands;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class PackageStateResultInterpreterTests
{
    private const string PackageName = "com.example.notes";

    [Fact]
    public void Interpret_AcceptsInstalledPackageWithExpectedHiddenState()
    {
        var result = CreateSuccess(new PackageStateResult(PackageName, true, true));

        var interpreted = PackageStateResultInterpreter.Interpret(result, PackageName, expectedHidden: true);

        Assert.True(interpreted.Succeeded);
    }

    [Fact]
    public void Interpret_RejectsFailedTransportResult()
    {
        var result = AndroidCommandResultEnvelope.Failure(
            Guid.NewGuid(),
            AndroidCommandKind.QueryPackageState,
            AndroidCommandTransportKind.SilentWorkProfile,
            "Work profile unavailable",
            "transport_failed",
            TimeSpan.Zero,
            string.Empty);

        var interpreted = PackageStateResultInterpreter.Interpret(result, PackageName, expectedHidden: true);

        Assert.False(interpreted.Succeeded);
        Assert.Equal("Work profile unavailable", interpreted.Message);
    }

    [Theory]
    [InlineData("com.example.other", true, true)]
    [InlineData(PackageName, false, true)]
    [InlineData(PackageName, true, false)]
    public void Interpret_RejectsMismatchedPackageState(string actualPackage, bool installed, bool hidden)
    {
        var result = CreateSuccess(new PackageStateResult(actualPackage, installed, hidden));

        var interpreted = PackageStateResultInterpreter.Interpret(result, PackageName, expectedHidden: true);

        Assert.False(interpreted.Succeeded);
    }

    [Fact]
    public void Interpret_RejectsResultFromUntrustedTransport()
    {
        var result = CreateSuccess(new PackageStateResult(PackageName, true, true)) with
        {
            Transport = AndroidCommandTransportKind.Activity
        };

        var interpreted = PackageStateResultInterpreter.Interpret(result, PackageName, expectedHidden: true);

        Assert.False(interpreted.Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-json")]
    public void Interpret_RejectsMissingOrMalformedPayload(string? payloadJson)
    {
        var result = CreateSuccess(new PackageStateResult(PackageName, true, true)) with
        {
            PayloadJson = payloadJson
        };

        var interpreted = PackageStateResultInterpreter.Interpret(result, PackageName, expectedHidden: true);

        Assert.False(interpreted.Succeeded);
    }

    private static AndroidCommandResultEnvelope CreateSuccess(PackageStateResult state)
    {
        return AndroidCommandResultEnvelope.Success(
            Guid.NewGuid(),
            AndroidCommandKind.QueryPackageState,
            AndroidCommandTransportKind.SilentWorkProfile,
            JsonSerializer.Serialize(state),
            "Package state queried",
            TimeSpan.Zero,
            string.Empty);
    }
}
