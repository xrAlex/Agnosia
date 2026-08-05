using Agnosia.Android.Commands;
using Agnosia.Android.Commands.Transports;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class AndroidCommandReplyTimeoutPolicyTests
{
    [Fact]
    public void GetReplyTimeout_QueryApps_UsesFullCommandTimeout()
    {
        var envelope = CreateEnvelope(AndroidCommandKind.QueryApps, TimeSpan.FromSeconds(30));

        var timeout = AndroidCommandReplyTimeoutPolicy.GetReplyTimeout(envelope);

        Assert.Equal(TimeSpan.FromSeconds(30), timeout);
    }

    [Fact]
    public void GetReplyTimeout_FastRefreshCommand_KeepsEarlyFallbackWindow()
    {
        var envelope = CreateEnvelope(AndroidCommandKind.QueryPermissions, TimeSpan.FromSeconds(10));

        var timeout = AndroidCommandReplyTimeoutPolicy.GetReplyTimeout(envelope);

        Assert.Equal(TimeSpan.FromMilliseconds(7500), timeout);
    }

    [Fact]
    public void GetReplyTimeout_FastRefreshCommand_UsesMinimumTimeout()
    {
        var envelope = CreateEnvelope(AndroidCommandKind.QueryPermissions, TimeSpan.FromMilliseconds(500));

        var timeout = AndroidCommandReplyTimeoutPolicy.GetReplyTimeout(envelope);

        Assert.Equal(TimeSpan.FromSeconds(1), timeout);
    }

    private static AndroidCommandEnvelope CreateEnvelope(AndroidCommandKind kind, TimeSpan timeout)
    {
        return new AndroidCommandEnvelope(
            Guid.NewGuid(),
            kind,
            AndroidCommandTargetProfile.Work,
            AndroidCommandInteractivity.Silent,
            AndroidCommandPriority.Refresh,
            timeout,
            null);
    }
}
