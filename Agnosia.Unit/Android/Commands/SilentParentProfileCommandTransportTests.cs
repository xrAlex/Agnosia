using Agnosia.Android.Commands;
using Agnosia.Android.Commands.Transports;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class SilentParentProfileCommandTransportTests
{
    [Fact]
    public async Task ExecuteAsync_OnNet10Target_ReturnsAndroidTargetRequiredFailure()
    {
        var transport = new SilentParentProfileCommandTransport();
        var envelope = new AndroidCommandEnvelope(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            AndroidCommandKind.WorkAppFrozen,
            AndroidCommandTargetProfile.Personal,
            AndroidCommandInteractivity.Silent,
            AndroidCommandPriority.Mutation,
            TimeSpan.FromSeconds(30),
            "{}");

        var result = await transport.ExecuteAsync(envelope, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AndroidCommandTransportKind.SilentParentProfile, result.Transport);
        Assert.Equal("android_target_required", result.ErrorCode);
        Assert.Contains("target=net10.0", result.Diagnostics, StringComparison.Ordinal);
    }
}
