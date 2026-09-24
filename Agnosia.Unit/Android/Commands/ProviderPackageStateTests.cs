using System.Text.Json;
using Agnosia.Android.Commands;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class ProviderPackageStateTests
{
    [Fact]
    public void AuthenticatedProviderStateCanConfirmDesiredHiddenState()
    {
        var result = AndroidCommandResultEnvelope.Success(Guid.NewGuid(), AndroidCommandKind.QueryPackageState,
            AndroidCommandTransportKind.Provider, JsonSerializer.Serialize(new PackageStateResult("test.app", true, true)),
            "ok", TimeSpan.Zero, "authenticated");
        Assert.True(PackageStateResultInterpreter.Interpret(result, "test.app", true).Succeeded);
        Assert.False(PackageStateResultInterpreter.Interpret(result, "other.app", true).Succeeded);
        Assert.False(PackageStateResultInterpreter.Interpret(result, "test.app", false).Succeeded);
    }
}
