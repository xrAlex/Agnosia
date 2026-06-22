using Agnosia.Android.Commands;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class AndroidCommandSchedulerTests
{
    [Fact]
    public async Task RunAsync_AllowsRefreshCommandsToReplaceOlderRefreshWork()
    {
        var scheduler = new AndroidCommandScheduler();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCanceled = false;

        var first = CreateEnvelope(AndroidCommandKind.QueryApps, AndroidCommandPriority.Refresh);
        var second = CreateEnvelope(AndroidCommandKind.QueryApps, AndroidCommandPriority.Refresh);

        var firstTask = scheduler.RunAsync(first, async cancellationToken =>
        {
            firstStarted.SetResult();
            try
            {
                await releaseFirst.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                firstCanceled = true;
                throw;
            }

            return "first";
        }, CancellationToken.None);

        await firstStarted.Task;

        var secondTask = scheduler.RunAsync(second, _ => Task.FromResult("second"), CancellationToken.None);

        await Assert.ThrowsAsync<OperationCanceledException>(() => firstTask);
        Assert.True(firstCanceled);
        Assert.Equal("second", await secondTask);
        releaseFirst.TrySetResult();
    }

    [Fact]
    public async Task RunAsync_DoesNotCancelDifferentRefreshKinds()
    {
        var scheduler = new AndroidCommandScheduler();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCanceled = false;

        var first = CreateEnvelope(AndroidCommandKind.QueryApps, AndroidCommandPriority.Refresh);
        var second = CreateEnvelope(AndroidCommandKind.QueryPermissions, AndroidCommandPriority.Refresh);

        var firstTask = scheduler.RunAsync(first, async cancellationToken =>
        {
            firstStarted.SetResult();
            try
            {
                await releaseFirst.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                firstCanceled = true;
                throw;
            }

            return "first";
        }, CancellationToken.None);

        await firstStarted.Task;

        var secondResult = await scheduler.RunAsync(second, _ => Task.FromResult("second"), CancellationToken.None);
        releaseFirst.SetResult();

        Assert.Equal("second", secondResult);
        Assert.Equal("first", await firstTask);
        Assert.False(firstCanceled);
    }

    [Fact]
    public async Task RunAsync_CancelsSameRefreshKindAndProfile()
    {
        var scheduler = new AndroidCommandScheduler();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = CreateEnvelope(AndroidCommandKind.QueryApps, AndroidCommandPriority.Refresh);
        var second = CreateEnvelope(AndroidCommandKind.QueryApps, AndroidCommandPriority.Refresh);

        var firstTask = scheduler.RunAsync(first, async cancellationToken =>
        {
            firstStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return "first";
        }, CancellationToken.None);

        await firstStarted.Task;
        var secondTask = scheduler.RunAsync(second, _ => Task.FromResult("second"), CancellationToken.None);

        await Assert.ThrowsAsync<OperationCanceledException>(() => firstTask);
        Assert.Equal("second", await secondTask);
    }

    [Fact]
    public async Task RunAsync_KeepsReplacedRefreshTokenUsableUntilFirstRefreshExits()
    {
        var scheduler = new AndroidCommandScheduler();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capturedToken = CancellationToken.None;

        var first = CreateEnvelope(AndroidCommandKind.QueryApps, AndroidCommandPriority.Refresh);
        var second = CreateEnvelope(AndroidCommandKind.QueryApps, AndroidCommandPriority.Refresh);

        var firstTask = scheduler.RunAsync(first, async cancellationToken =>
        {
            capturedToken = cancellationToken;
            firstStarted.SetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                using var registration = capturedToken.Register(static () => { });
                throw;
            }

            return "first";
        }, CancellationToken.None);

        await firstStarted.Task;

        var secondTask = scheduler.RunAsync(second, _ => Task.FromResult("second"), CancellationToken.None);

        await Assert.ThrowsAsync<OperationCanceledException>(() => firstTask);
        Assert.Equal("second", await secondTask);
    }

    [Fact]
    public async Task RunAsync_ContinuesWhenPreviousRefreshCancellationCallbackThrows()
    {
        var scheduler = new AndroidCommandScheduler();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration throwingRegistration = default;

        var first = CreateEnvelope(AndroidCommandKind.QueryApps, AndroidCommandPriority.Refresh);
        var second = CreateEnvelope(AndroidCommandKind.QueryApps, AndroidCommandPriority.Refresh);
        var third = CreateEnvelope(AndroidCommandKind.QueryApps, AndroidCommandPriority.Refresh);

        var firstTask = scheduler.RunAsync(first, async cancellationToken =>
        {
            throwingRegistration = cancellationToken.Register(
                static () => throw new InvalidOperationException("Cancellation callback failed."));
            firstStarted.SetResult();

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return "first";
        }, CancellationToken.None);

        await firstStarted.Task;

        var secondResult = await scheduler.RunAsync(second, _ => Task.FromResult("second"), CancellationToken.None);
        var thirdResult = await scheduler.RunAsync(third, _ => Task.FromResult("third"), CancellationToken.None);

        Assert.Equal("second", secondResult);
        Assert.Equal("third", thirdResult);
        await Assert.ThrowsAsync<OperationCanceledException>(() => firstTask);
        throwingRegistration.Dispose();
    }

    [Fact]
    public async Task RunAsync_AllowsPreviousRefreshCancellationCallbackToReenterScheduler()
    {
        var scheduler = new AndroidCommandScheduler();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration reentrantRegistration = default;

        var first = CreateEnvelope(AndroidCommandKind.QueryApps, AndroidCommandPriority.Refresh);
        var second = CreateEnvelope(AndroidCommandKind.QueryApps, AndroidCommandPriority.Refresh);
        var third = CreateEnvelope(AndroidCommandKind.QueryApps, AndroidCommandPriority.Refresh);

        var firstTask = scheduler.RunAsync(first, async cancellationToken =>
        {
            reentrantRegistration = cancellationToken.Register(() =>
            {
                var thirdResult = scheduler.RunAsync(
                    third,
                    _ => Task.FromResult("third"),
                    CancellationToken.None).GetAwaiter().GetResult();
                Assert.Equal("third", thirdResult);
            });
            firstStarted.SetResult();

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return "first";
        }, CancellationToken.None);

        await firstStarted.Task;

        var secondTask = scheduler.RunAsync(second, _ => Task.FromResult("second"), CancellationToken.None);

        Assert.Equal(
            "second",
            await secondTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<OperationCanceledException>(() => firstTask);
        reentrantRegistration.Dispose();
    }

    [Fact]
    public async Task RunAsync_SerializesMutationCommands()
    {
        var scheduler = new AndroidCommandScheduler();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = false;

        var first = CreateEnvelope(AndroidCommandKind.FreezePackage, AndroidCommandPriority.Mutation);
        var second = CreateEnvelope(AndroidCommandKind.UnfreezePackage, AndroidCommandPriority.Mutation);

        var firstTask = scheduler.RunAsync(first, async _ =>
        {
            firstStarted.SetResult();
            await releaseFirst.Task;
            return "first";
        }, CancellationToken.None);

        await firstStarted.Task;

        var secondTask = scheduler.RunAsync(second, _ =>
        {
            secondStarted = true;
            return Task.FromResult("second");
        }, CancellationToken.None);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(secondStarted);

        releaseFirst.SetResult();

        Assert.Equal("first", await firstTask);
        Assert.Equal("second", await secondTask);
        Assert.True(secondStarted);
    }

    private static AndroidCommandEnvelope CreateEnvelope(
        AndroidCommandKind kind,
        AndroidCommandPriority priority)
    {
        return new AndroidCommandEnvelope(
            Guid.NewGuid(),
            kind,
            AndroidCommandTargetProfile.Work,
            AndroidCommandInteractivity.Silent,
            priority,
            TimeSpan.FromSeconds(30),
            null);
    }

}
