using Agnosia.Android.Commands;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class AndroidCommandCenterTests
{
    [Fact]
    public async Task ExecuteAsync_UsesFirstSuccessfulTransport()
    {
        var envelope = CreateEnvelope(AndroidCommandKind.QueryPermissions);
        var center = new AndroidCommandCenter(
            new AndroidCommandScheduler(),
            [
                new TestTransport(AndroidCommandTransportKind.SilentWorkProfile, succeeds: true),
                new TestTransport(AndroidCommandTransportKind.Activity, succeeds: true)
            ]);

        var result = await center.ExecuteAsync(envelope, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(AndroidCommandTransportKind.SilentWorkProfile, result.Transport);
        Assert.Equal("SilentWorkProfile ok", result.Message);
    }

    [Fact]
    public async Task ExecuteAsync_FallsBackToActivity_WhenWorkSilentTransportIsUnavailable()
    {
        var envelope = CreateEnvelope(AndroidCommandKind.QueryPermissions);
        var center = new AndroidCommandCenter(
            new AndroidCommandScheduler(),
            [
                new TestTransport(AndroidCommandTransportKind.SilentWorkProfile, succeeds: false),
                new TestTransport(AndroidCommandTransportKind.Activity, succeeds: true)
            ]);

        var result = await center.ExecuteAsync(envelope, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(AndroidCommandTransportKind.Activity, result.Transport);
        Assert.Contains("fallbackFrom=SilentWorkProfile", result.Diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsLastFailureWhenAllTransportsFail()
    {
        var envelope = CreateEnvelope(AndroidCommandKind.QueryPermissions);
        var center = new AndroidCommandCenter(
            new AndroidCommandScheduler(),
            [
                new TestTransport(AndroidCommandTransportKind.SilentWorkProfile, succeeds: false),
                new TestTransport(AndroidCommandTransportKind.Activity, succeeds: false)
            ]);

        var result = await center.ExecuteAsync(envelope, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(AndroidCommandTransportKind.Activity, result.Transport);
        Assert.Equal("Activity failed", result.Message);
    }

    [Fact]
    public async Task ExecuteAsync_FallsBackWhenTransportThrows()
    {
        var envelope = CreateEnvelope(AndroidCommandKind.QueryPermissions);
        var center = new AndroidCommandCenter(
            new AndroidCommandScheduler(),
            [
                new ThrowingTransport(AndroidCommandTransportKind.SilentWorkProfile),
                new TestTransport(AndroidCommandTransportKind.Activity, succeeds: true)
            ]);

        var result = await center.ExecuteAsync(envelope, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(AndroidCommandTransportKind.Activity, result.Transport);
        Assert.Contains("fallbackFrom=SilentWorkProfile", result.Diagnostics, StringComparison.Ordinal);
        Assert.Contains("transport_exception", result.Diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsTimeoutFailure_WhenEnvelopeTimeoutExpires()
    {
        var envelope = new AndroidCommandEnvelope(
            Guid.NewGuid(),
            AndroidCommandKind.QueryPermissions,
            AndroidCommandTargetProfile.Work,
            AndroidCommandInteractivity.Silent,
            AndroidCommandPriority.UserBlocking,
            TimeSpan.FromMilliseconds(25),
            null);
        var center = new AndroidCommandCenter(
            new AndroidCommandScheduler(),
            [new HangingTransport(AndroidCommandTransportKind.SilentWorkProfile)]);

        var result = await center.ExecuteAsync(envelope, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("command_timeout", result.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCallerCancellation()
    {
        var envelope = CreateEnvelope(AndroidCommandKind.QueryPermissions);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var center = new AndroidCommandCenter(
            new AndroidCommandScheduler(),
            [new CancelingTransport(AndroidCommandTransportKind.SilentWorkProfile)]);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            center.ExecuteAsync(envelope, cancellation.Token));
    }

    [Fact]
    public async Task ExecuteAsync_PreservesTransportDiagnostics()
    {
        var envelope = CreateEnvelope(AndroidCommandKind.QueryPermissions);
        var center = new AndroidCommandCenter(
            new AndroidCommandScheduler(),
            [new TestTransport(AndroidCommandTransportKind.SilentWorkProfile, succeeds: true, "actualProfile=Work")]);

        var result = await center.ExecuteAsync(envelope, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("actualProfile=Work", result.Diagnostics, StringComparison.Ordinal);
    }

    private static AndroidCommandEnvelope CreateEnvelope(AndroidCommandKind kind)
    {
        return new AndroidCommandEnvelope(
            Guid.NewGuid(),
            kind,
            AndroidCommandTargetProfile.Work,
            AndroidCommandInteractivity.Silent,
            AndroidCommandPriority.Refresh,
            TimeSpan.FromSeconds(30),
            null);
    }

    private sealed class TestTransport(
        AndroidCommandTransportKind kind,
        bool succeeds,
        string diagnostics = "") : IAndroidCommandTransport
    {
        public AndroidCommandTransportKind Kind => kind;

        public Task<AndroidCommandResultEnvelope> ExecuteAsync(
            AndroidCommandEnvelope envelope,
            CancellationToken cancellationToken)
        {
            var result = succeeds
                ? AndroidCommandResultEnvelope.Success(
                    envelope.CorrelationId,
                    envelope.Kind,
                    kind,
                    null,
                    $"{kind} ok",
                    TimeSpan.FromMilliseconds(1),
                    diagnostics)
                : AndroidCommandResultEnvelope.Failure(
                    envelope.CorrelationId,
                    envelope.Kind,
                    kind,
                    $"{kind} failed",
                    "transport_failed",
                    TimeSpan.FromMilliseconds(1),
                    diagnostics);

            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingTransport(AndroidCommandTransportKind kind) : IAndroidCommandTransport
    {
        public AndroidCommandTransportKind Kind => kind;

        public Task<AndroidCommandResultEnvelope> ExecuteAsync(
            AndroidCommandEnvelope envelope,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Transport failed unexpectedly.");
        }
    }

    private sealed class CancelingTransport(AndroidCommandTransportKind kind) : IAndroidCommandTransport
    {
        public AndroidCommandTransportKind Kind => kind;

        public Task<AndroidCommandResultEnvelope> ExecuteAsync(
            AndroidCommandEnvelope envelope,
            CancellationToken cancellationToken)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private sealed class HangingTransport(AndroidCommandTransportKind kind) : IAndroidCommandTransport
    {
        public AndroidCommandTransportKind Kind => kind;

        public async Task<AndroidCommandResultEnvelope> ExecuteAsync(
            AndroidCommandEnvelope envelope,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }
}
