using Agnosia.Android.Commands;
using Agnosia.Models;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class ProviderRoutingTests
{
    [Theory]
    [InlineData(AndroidCommandKind.QueryApps, true)]
    [InlineData(AndroidCommandKind.QueryPermissions, true)]
    [InlineData(AndroidCommandKind.FreezePackage, true)]
    [InlineData(AndroidCommandKind.UnfreezePackage, true)]
    [InlineData(AndroidCommandKind.InstallPackage, false)]
    [InlineData(AndroidCommandKind.StartFileShuttleParentToWork, false)]
    [InlineData(AndroidCommandKind.SetLockdownEnabled, false)]
    public void EnabledProviderUsesPositiveAllowlist(int value, bool supported)
    {
        var envelope = Envelope((AndroidCommandKind)value);
        Assert.Equal(supported
                ? [AndroidCommandTransportKind.Provider, AndroidCommandTransportKind.Activity]
                : [AndroidCommandTransportKind.Activity],
            AndroidCommandRouter.GetRoute(envelope, true).Transports);
        Assert.Equal([AndroidCommandTransportKind.Activity], AndroidCommandRouter.GetRoute(envelope, false).Transports);
        Assert.Equal([AndroidCommandTransportKind.Activity], AndroidCommandRouter.GetRoute(
            envelope with { Interactivity = AndroidCommandInteractivity.Interactive }, true).Transports);
    }

    [Fact]
    public async Task ReadFailureFallsBackToActivity()
    {
        var activity = new RecordingTransport(AndroidCommandTransportKind.Activity, succeeds: true);
        var provider = new RecordingTransport(AndroidCommandTransportKind.Provider, errorCode: "authentication_failed");
        var center = new AndroidCommandCenter(new AndroidCommandScheduler(), [provider, activity], () => true);
        var result = await center.ExecuteAsync(Envelope(AndroidCommandKind.QueryApps), TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded);
        Assert.Equal(AndroidCommandTransportKind.Activity, result.Transport);
        Assert.Contains("fallbackFrom=Provider", result.Diagnostics);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(1, activity.Calls);
    }

    [Fact]
    public async Task MutationWithPreDispatchFailureFallsBackToActivity()
    {
        var activity = new RecordingTransport(AndroidCommandTransportKind.Activity, succeeds: true);
        var provider = new RecordingTransport(AndroidCommandTransportKind.Provider, errorCode: "grant_missing");
        var center = new AndroidCommandCenter(new AndroidCommandScheduler(), [provider, activity], () => true);

        var result = await center.ExecuteAsync(Envelope(AndroidCommandKind.FreezePackage), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(1, activity.Calls);
    }

    [Theory]
    [InlineData("outcome_unknown")]
    [InlineData("package_policy_failed")]
    [InlineData("transport_exception")]
    public async Task MutationWithoutProofOfNoDispatchDoesNotRetry(string errorCode)
    {
        var activity = new RecordingTransport(AndroidCommandTransportKind.Activity, succeeds: true);
        var provider = new RecordingTransport(AndroidCommandTransportKind.Provider, errorCode: errorCode);
        var center = new AndroidCommandCenter(new AndroidCommandScheduler(), [provider, activity], () => true);

        var result = await center.ExecuteAsync(Envelope(AndroidCommandKind.FreezePackage), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(errorCode, result.ErrorCode);
        Assert.Equal(0, activity.Calls);
    }

    [Fact]
    public void ManualModesPinTheWorkTransport()
    {
        var query = Envelope(AndroidCommandKind.QueryApps);
        Assert.Equal([AndroidCommandTransportKind.Provider], AndroidCommandRouter.GetRoute(
            query, true, CommandTransportPreference.Provider).Transports);
        Assert.Equal([AndroidCommandTransportKind.Activity], AndroidCommandRouter.GetRoute(
            query, true, CommandTransportPreference.Activity).Transports);
        Assert.Equal([AndroidCommandTransportKind.Provider], AndroidCommandRouter.GetRoute(
            Envelope(AndroidCommandKind.InstallPackage), true, CommandTransportPreference.Provider).Transports);
        Assert.Equal([AndroidCommandTransportKind.Activity], AndroidCommandRouter.GetRoute(
            query, false, CommandTransportPreference.Auto).Transports);
    }

    [Fact]
    public async Task PinnedProviderFailureDoesNotUseActivity()
    {
        var activity = new RecordingTransport(AndroidCommandTransportKind.Activity, succeeds: true);
        var provider = new RecordingTransport(AndroidCommandTransportKind.Provider, errorCode: "grant_missing");
        var center = new AndroidCommandCenter(new AndroidCommandScheduler(), [provider, activity],
            () => true, () => CommandTransportPreference.Provider);

        var result = await center.ExecuteAsync(Envelope(AndroidCommandKind.QueryApps), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(0, activity.Calls);
    }

    [Fact]
    public async Task PinnedActivityDoesNotProbeProviderAvailability()
    {
        var activity = new RecordingTransport(AndroidCommandTransportKind.Activity, succeeds: true);
        var provider = new RecordingTransport(AndroidCommandTransportKind.Provider, succeeds: true);
        var center = new AndroidCommandCenter(new AndroidCommandScheduler(), [provider, activity],
            () => throw new InvalidOperationException("Provider availability must not be checked."),
            () => CommandTransportPreference.Activity);

        var result = await center.ExecuteAsync(Envelope(AndroidCommandKind.QueryApps), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(0, provider.Calls);
        Assert.Equal(1, activity.Calls);
    }

    [Fact]
    public async Task AutoFallsBackWhenProviderReadHangs()
    {
        var activity = new RecordingTransport(AndroidCommandTransportKind.Activity, succeeds: true);
        var center = new AndroidCommandCenter(new AndroidCommandScheduler(),
            [new HangingProviderTransport(), activity], () => true);
        var envelope = Envelope(AndroidCommandKind.QueryApps) with { Timeout = TimeSpan.FromSeconds(2) };

        var result = await center.ExecuteAsync(envelope, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(AndroidCommandTransportKind.Activity, result.Transport);
        Assert.Equal(1, activity.Calls);
    }

    private static AndroidCommandEnvelope Envelope(AndroidCommandKind kind) => new(Guid.NewGuid(), kind,
        AndroidCommandTargetProfile.Work, AndroidCommandInteractivity.NonInteractive,
        AndroidCommandPriority.UserBlocking, TimeSpan.FromSeconds(30), null);

    private sealed class RecordingTransport(AndroidCommandTransportKind kind, bool succeeds = false,
        string errorCode = "authentication_failed") : IAndroidCommandTransport
    {
        public AndroidCommandTransportKind Kind => kind;
        public int Calls { get; private set; }
        public Task<AndroidCommandResultEnvelope> ExecuteAsync(AndroidCommandEnvelope envelope, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(succeeds
                ? AndroidCommandResultEnvelope.Success(envelope.CorrelationId, envelope.Kind, Kind,
                    null, "ok", TimeSpan.Zero, "")
                : AndroidCommandResultEnvelope.Failure(envelope.CorrelationId, envelope.Kind,
                    Kind, "denied", errorCode, TimeSpan.Zero, ""));
        }
    }

    private sealed class HangingProviderTransport : IAndroidCommandTransport
    {
        public AndroidCommandTransportKind Kind => AndroidCommandTransportKind.Provider;

        public async Task<AndroidCommandResultEnvelope> ExecuteAsync(
            AndroidCommandEnvelope envelope, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }
}
