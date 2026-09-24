using Agnosia.Android.Commands;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class ProviderConcurrencyTests
{
    [Fact]
    public void FinalDispatchCancellationCheckMustAlreadyExposePossibleDispatch()
    {
        var dispatch = new CommandDispatchState();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;
        Assert.Throws<OperationCanceledException>(() => dispatch.Invoke(() => ++calls, cancellation.Token));
        Assert.Equal(0, calls);
        // This conservative marker must precede the final token check: its waiter runs independently.
        Assert.True(dispatch.MayHaveDispatched);
    }

    [Fact]
    public async Task OrderedQueryCannotOvertakeMutationWhoseCallerStoppedWaiting()
    {
        var executor = new BoundedCommandExecutor(2);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var state = 0;
        var mutation = executor.RunAsync(() => { entered.SetResult(); release.Wait(); return Interlocked.Exchange(ref state, 1); }, cancellation.Token, ordered: true);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mutation);
        var query = executor.RunAsync(() => Volatile.Read(ref state), TestContext.Current.CancellationToken, ordered: true);
        try
        {
            // Give the second physical worker time to expose an incorrectly released ordering gate.
            await Task.Delay(50, TestContext.Current.CancellationToken);
            Assert.False(query.IsCompleted);
        }
        finally { release.Set(); }
        Assert.Equal(1, await query.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancelledWaiterDoesNotReleaseNativeSlotOrDispatchQueuedCall()
    {
        var executor = new BoundedCommandExecutor(1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var firstCancellation = new CancellationTokenSource();
        var first = executor.RunAsync(() => { entered.SetResult(); release.Wait(); return 1; }, firstCancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        firstCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        using var queuedCancellation = new CancellationTokenSource();
        var dispatched = false;
        var second = executor.RunAsync(() => { dispatched = true; return 2; }, queuedCancellation.Token);
        queuedCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.False(dispatched);
        release.Set();
        Assert.Equal(3, await executor.RunAsync(() => 3, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.False(dispatched);
    }

    [Fact]
    public async Task ConnectionIsSharedAndCancellingOneWaiterDoesNotCancelOthers()
    {
        var flight = new CommandSingleFlight<int>();
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        Task<int> Connect() { calls++; return completion.Task; }
        using var cancellation = new CancellationTokenSource();
        var first = flight.RunAsync(Connect, cancellation.Token);
        var second = flight.RunAsync(Connect, CancellationToken.None);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        completion.SetResult(5);
        Assert.Equal(5, await second);
        Assert.Equal(1, calls);
    }
}
