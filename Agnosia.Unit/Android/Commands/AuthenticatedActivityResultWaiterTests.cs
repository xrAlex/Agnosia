using Agnosia.Android.Commands;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class AuthenticatedActivityResultWaiterTests
{
    [Fact]
    public async Task Empty_activity_result_does_not_complete_before_authenticated_callback()
    {
        var callback = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = AuthenticatedActivityResultWaiter.WaitAsync(Task.FromResult("unsigned"),
            callback.Task, value => value == "signed", CancellationToken.None);
        Assert.False(pending.IsCompleted);

        callback.SetResult("signed");
        Assert.Equal("signed", await pending);
    }

    [Fact]
    public async Task Authenticated_activity_result_completes_when_callback_is_missing()
    {
        var callback = new TaskCompletionSource<string>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Assert.Equal("signed", await AuthenticatedActivityResultWaiter.WaitAsync(
            Task.FromResult("signed"), callback.Task, value => value == "signed", timeout.Token));
    }

    [Fact]
    public async Task Callback_completes_while_activity_result_is_delayed()
    {
        var activity = new TaskCompletionSource<string>();
        Assert.Equal("signed", await AuthenticatedActivityResultWaiter.WaitAsync(
            activity.Task, Task.FromResult("signed"), _ => false, CancellationToken.None));
    }

    [Fact]
    public async Task Received_callback_wins_over_simultaneous_activity_cancellation()
    {
        Assert.Equal("signed", await AuthenticatedActivityResultWaiter.WaitAsync(
            Task.FromCanceled<string>(new CancellationToken(true)), Task.FromResult("signed"),
            _ => false, CancellationToken.None));
    }

    [Fact]
    public async Task Missing_results_observe_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var pending = AuthenticatedActivityResultWaiter.WaitAsync(new TaskCompletionSource<string>().Task,
            new TaskCompletionSource<string>().Task, _ => false, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }
}
