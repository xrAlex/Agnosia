using Agnosia.Android.Commands;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class AndroidCommandRouterTests
{
    [Fact]
    public void GetRoute_SilentWorkCommand_UsesWorkSilentThenActivityFallback()
    {
        var envelope = CreateEnvelope(
            AndroidCommandKind.QueryApps,
            AndroidCommandTargetProfile.Work,
            AndroidCommandInteractivity.Silent,
            AndroidCommandPriority.Refresh);

        var route = AndroidCommandRouter.GetRoute(envelope);

        Assert.Equal(
            [AndroidCommandTransportKind.SilentWorkProfile, AndroidCommandTransportKind.Activity],
            route.Transports);
    }

    [Fact]
    public void GetRoute_SilentPersonalCommand_DoesNotUseWorkSilentTransport()
    {
        var envelope = CreateEnvelope(
            AndroidCommandKind.QueryLogs,
            AndroidCommandTargetProfile.Personal,
            AndroidCommandInteractivity.Silent,
            AndroidCommandPriority.Refresh);

        var route = AndroidCommandRouter.GetRoute(envelope);

        Assert.Equal([AndroidCommandTransportKind.DirectLocal], route.Transports);
    }

    [Fact]
    public void GetRoute_InteractiveWorkCommand_UsesActivityOnly()
    {
        var envelope = CreateEnvelope(
            AndroidCommandKind.RequestUsageStatsAccess,
            AndroidCommandTargetProfile.Work,
            AndroidCommandInteractivity.Interactive,
            AndroidCommandPriority.UserBlocking);

        var route = AndroidCommandRouter.GetRoute(envelope);

        Assert.Equal([AndroidCommandTransportKind.Activity], route.Transports);
    }

    private static AndroidCommandEnvelope CreateEnvelope(
        AndroidCommandKind kind,
        AndroidCommandTargetProfile targetProfile,
        AndroidCommandInteractivity interactivity,
        AndroidCommandPriority priority)
    {
        return new AndroidCommandEnvelope(
            Guid.NewGuid(),
            kind,
            targetProfile,
            interactivity,
            priority,
            TimeSpan.FromSeconds(30),
            null);
    }
}
