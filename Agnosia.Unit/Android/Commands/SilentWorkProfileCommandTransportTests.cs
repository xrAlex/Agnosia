using Agnosia.Android.Commands;
using Agnosia.Android.Commands.Transports;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class SilentWorkProfileCommandTransportTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsExplicitUnsupportedCapabilityFailure()
    {
        var transport = new SilentWorkProfileCommandTransport();
        var envelope = new AndroidCommandEnvelope(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            AndroidCommandKind.QueryApps,
            AndroidCommandTargetProfile.Work,
            AndroidCommandInteractivity.Silent,
            AndroidCommandPriority.Refresh,
            TimeSpan.FromSeconds(30),
            null);

        var result = await transport.ExecuteAsync(envelope, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AndroidCommandTransportKind.SilentWorkProfile, result.Transport);
        Assert.Equal("silent_work_transport_unavailable", result.ErrorCode);
        Assert.Contains("capability=unsupported", result.Diagnostics, StringComparison.Ordinal);
        Assert.Contains("fallback=activity", result.Diagnostics, StringComparison.Ordinal);
    }
}
