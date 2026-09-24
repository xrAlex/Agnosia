using Agnosia.Services;
using Xunit;

namespace Agnosia.Unit.Services;

public sealed class CoalescingRefreshWorkerTests
{
    [Fact]
    public async Task MutationFollowUpDoesNotRepeatAlreadyReadPermissions()
    {
        var worker = new CoalescingRefreshWorker();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<bool>();
        async Task Refresh(bool permissions) { calls.Add(permissions); entered.TrySetResult(); await release.Task; }
        var first = worker.RunAsync(Refresh);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var mutation = worker.RunAsync(Refresh, stateChanged: true, includePermissions: false);
        release.SetResult();
        await Task.WhenAll(first, mutation);
        Assert.Equal([true, false], calls);
    }

    [Fact]
    public async Task SameGenerationRequestsShareOneRefresh()
    {
        var worker = new CoalescingRefreshWorker();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async Task Refresh(bool _) { Interlocked.Increment(ref calls); entered.TrySetResult(); await release.Task; }
        var first = worker.RunAsync(Refresh);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var requests = Enumerable.Range(0, 20).Select(_ => worker.RunAsync(Refresh)).ToArray();
        release.SetResult();
        await Task.WhenAll(requests.Append(first));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task StateChangesDuringRefreshProduceOneFollowUpAndKeepPermissionRefresh()
    {
        var worker = new CoalescingRefreshWorker();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<bool>();
        async Task Refresh(bool permissions)
        {
            requests.Add(permissions);
            entered.TrySetResult();
            await release.Task;
        }
        var first = worker.RunAsync(Refresh, includePermissions: false);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var changed = worker.RunAsync(Refresh, stateChanged: true, includePermissions: false);
        var changedAgain = worker.RunAsync(Refresh, stateChanged: true, includePermissions: false);
        var permissionReturn = worker.RunAsync(Refresh, includePermissions: true);
        release.SetResult();
        await Task.WhenAll(first, changed, changedAgain, permissionReturn);
        Assert.Equal([false, true], requests);
    }
}
