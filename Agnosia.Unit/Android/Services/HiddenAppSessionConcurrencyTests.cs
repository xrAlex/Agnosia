using Agnosia.Android.Services;
using Xunit;

namespace Agnosia.Unit.Android.Services;

public sealed class HiddenAppSessionConcurrencyTests
{
    // Catches reading a replaced/disposed CTS field after the queued monitor delegate starts.
    [Fact]
    public async Task QueueMonitor_captures_the_token_supplied_when_work_is_queued()
    {
        Func<Task>? queuedWork = null;
        var firstCancellation = new CancellationTokenSource();
        var secondCancellation = new CancellationTokenSource();
        var expectedToken = firstCancellation.Token;
        CancellationToken observed = default;

        await HiddenAppSessionConcurrency.QueueMonitor(
            expectedToken,
            token =>
            {
                observed = token;
                return Task.CompletedTask;
            },
            work =>
            {
                queuedWork = work;
                return Task.CompletedTask;
            });

        firstCancellation.Cancel();
        firstCancellation.Dispose();
        _ = secondCancellation.Token;
        await Assert.IsType<Func<Task>>(queuedWork)();

        Assert.True(observed.IsCancellationRequested);
        Assert.Equal(expectedToken, observed);
    }

    // Catches launch/re-hide overlap by proving the shared operation lease is exclusive.
    [Fact]
    public async Task EnterOperationAsync_waits_until_the_current_operation_releases()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var first = await HiddenAppSessionConcurrency.EnterOperationAsync(cancellationToken);

        var secondTask = HiddenAppSessionConcurrency.EnterOperationAsync(cancellationToken).AsTask();

        Assert.False(secondTask.IsCompleted);
        first.Dispose();
        using var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
    }

    // Catches a main-thread receiver blocking behind an asynchronous launch operation.
    [Fact]
    public async Task TryEnterOperation_returns_immediately_when_an_operation_is_active()
    {
        using var active = await HiddenAppSessionConcurrency.EnterOperationAsync(
            TestContext.Current.CancellationToken);

        var entered = HiddenAppSessionConcurrency.TryEnterOperation(out var lease);

        Assert.False(entered);
        Assert.Null(lease);
    }
}
