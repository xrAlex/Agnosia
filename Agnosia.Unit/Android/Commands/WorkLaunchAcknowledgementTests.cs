using Agnosia.Android.Commands;
using Agnosia.Models;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class WorkLaunchAcknowledgementTests
{
    [Theory]
    [InlineData("old", "target")]
    [InlineData("launch", "other")]
    [InlineData("", "target")]
    [InlineData("launch", "")]
    public void Rejects_mismatched_and_missing_identity(string launchId, string packageName)
    {
        var waiter = new WorkLaunchAcknowledgementWaiter("launch", "target");
        Assert.False(waiter.TryAccept(new(launchId, packageName, true, "attempted")));
        Assert.False(waiter.Task.IsCompleted);
    }

    [Fact]
    public async Task Callback_before_await_is_accepted_once_including_failure()
    {
        var waiter = new WorkLaunchAcknowledgementWaiter("launch", "target");
        Assert.True(waiter.TryAccept(new("launch", "target", false, "no activity")));
        Assert.False(waiter.TryAccept(new("launch", "target", true, "duplicate")));
        Assert.False((await waiter.Task).Succeeded);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Definite_dispatch_refusal_returns_failure_without_waiting_for_callback(bool throws)
    {
        var waiter = new WorkLaunchAcknowledgementWaiter("launch", "target");
        var result = await waiter.DispatchAndWaitAsync(_ => throws
            ? throw new InvalidOperationException("activity unavailable")
            : Task.FromResult(OperationResult.Failure("activity unavailable")), TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        Assert.False(waiter.Task.IsCompleted);
    }

    [Fact]
    public async Task Cancellation_after_dispatch_is_unknown_and_does_not_invent_failure_acknowledgement()
    {
        var waiter = new WorkLaunchAcknowledgementWaiter("launch", "target");
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAsync<WorkLaunchUnconfirmedException>(() => waiter.DispatchAndWaitAsync(_ =>
        {
            cancellation.Cancel();
            return Task.FromResult(OperationResult.Success("sent"));
        }, cancellation.Token));
        Assert.False(waiter.Task.IsCompleted);
    }
}
