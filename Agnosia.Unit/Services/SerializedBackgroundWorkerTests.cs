using Agnosia.Services;
using Xunit;

namespace Agnosia.Unit.Services;

public sealed class SerializedBackgroundWorkerTests
{
    [Fact]
    public async Task RunAsync_serializes_concurrent_actions()
    {
        var worker = new SerializedBackgroundWorker();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = worker.RunAsync(async () =>
        {
            firstEntered.SetResult();
            await releaseFirst.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);
        }, TestContext.Current.CancellationToken);

        await firstEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        var second = worker.RunAsync(() =>
        {
            secondEntered.SetResult();
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        Assert.False(secondEntered.Task.IsCompleted);

        releaseFirst.SetResult();

        await secondEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        await Task.WhenAll(first, second).WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunAsync_returns_action_result()
    {
        var worker = new SerializedBackgroundWorker();

        var result = await worker.RunAsync(() => Task.FromResult(42), TestContext.Current.CancellationToken);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task RunAsync_rejects_null_action()
    {
        var worker = new SerializedBackgroundWorker();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            worker.RunAsync(null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            worker.RunAsync<int>(null!, TestContext.Current.CancellationToken));
    }
}
