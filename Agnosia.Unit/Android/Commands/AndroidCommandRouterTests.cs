using Agnosia.Android.Commands;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class AndroidCommandRouterTests
{
    [Fact]
    public void GetRoute_AuthenticationRecovery_UsesOnlyBoundWorkProfileTransport()
    {
        var envelope = CreateEnvelope(
            AndroidCommandKind.RecoverAuthentication,
            AndroidCommandTargetProfile.Work,
            AndroidCommandInteractivity.Silent,
            AndroidCommandPriority.UserBlocking);

        var route = AndroidCommandRouter.GetRoute(envelope);

        Assert.Equal([AndroidCommandTransportKind.SilentWorkProfile], route.Transports);
    }

    [Fact]
    public void GetRoute_PackageStateQuery_UsesOnlyBoundWorkProfileTransport()
    {
        var envelope = CreateEnvelope(
            AndroidCommandKind.QueryPackageState,
            AndroidCommandTargetProfile.Work,
            AndroidCommandInteractivity.Silent,
            AndroidCommandPriority.UserBlocking);

        var route = AndroidCommandRouter.GetRoute(envelope);

        Assert.Equal([AndroidCommandTransportKind.SilentWorkProfile], route.Transports);
    }

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

    // Ловит возврат к BAL-зависимому Activity fallback для durable work-freeze callback.
    [Fact]
    public void GetRoute_WorkAppFrozenToPersonal_UsesOnlySilentParentTransport()
    {
        var envelope = CreateEnvelope(
            AndroidCommandKind.WorkAppFrozen,
            AndroidCommandTargetProfile.Personal,
            AndroidCommandInteractivity.Silent,
            AndroidCommandPriority.Mutation);

        var route = AndroidCommandRouter.GetRoute(envelope);

        Assert.Equal([AndroidCommandTransportKind.SilentParentProfile], route.Transports);
    }

    [Fact]
    public void GetRoute_WorkAppFrozenWithInvalidDirectionOrInteractivity_HasNoFallback()
    {
        var invalidRequests = new[]
        {
            CreateEnvelope(
                AndroidCommandKind.WorkAppFrozen,
                AndroidCommandTargetProfile.Work,
                AndroidCommandInteractivity.Silent,
                AndroidCommandPriority.Mutation),
            CreateEnvelope(
                AndroidCommandKind.WorkAppFrozen,
                AndroidCommandTargetProfile.Personal,
                AndroidCommandInteractivity.Interactive,
                AndroidCommandPriority.Mutation)
        };

        foreach (var request in invalidRequests)
            Assert.Empty(AndroidCommandRouter.GetRoute(request).Transports);
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
