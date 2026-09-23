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

}
