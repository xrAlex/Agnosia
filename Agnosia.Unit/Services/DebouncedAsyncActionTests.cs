using Agnosia.Services;
using Agnosia.Unit.TestSupport;
using Xunit;

namespace Agnosia.Unit.Services;

public sealed class DebouncedAsyncActionTests
{
    // Проверяет, что debounce action не принимает отрицательную задержку.
    [Fact]
    public void Constructor_rejects_negative_delay()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DebouncedAsyncAction(TimeSpan.FromMilliseconds(-1)));
    }

    // Проверяет защиту от null action при постановке задачи.
    [Fact]
    public void Schedule_rejects_null_action()
    {
        var debouncedAction = new DebouncedAsyncAction(TimeSpan.Zero);

        Assert.Throws<ArgumentNullException>(() => debouncedAction.Schedule(null!));
    }

    // Проверяет, что запланированная задача выполняется после задержки.
    [Fact]
    public async Task Schedule_runs_action_after_delay()
    {
        var delays = new ManualDelayScheduler();
        var completion = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var debouncedAction = new DebouncedAsyncAction(
            TimeSpan.FromMilliseconds(10),
            null,
            delays.DelayAsync);

        debouncedAction.Schedule(token =>
        {
            completion.SetResult(token);
            return Task.CompletedTask;
        });

        Assert.Equal([TimeSpan.FromMilliseconds(10)], delays.RequestedDelays);
        delays.CompleteNext();
        var token = await completion.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.False(token.IsCancellationRequested);
    }

    // Проверяет, что новая постановка отменяет предыдущую ожидающую задачу.
    [Fact]
    public async Task Schedule_cancels_previous_pending_action()
    {
        var delays = new ManualDelayScheduler();
        var calls = new List<string>();
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var debouncedAction = new DebouncedAsyncAction(
            TimeSpan.FromMilliseconds(50),
            null,
            delays.DelayAsync);

        debouncedAction.Schedule(_ =>
        {
            calls.Add("first");
            return Task.CompletedTask;
        });
        debouncedAction.Schedule(_ =>
        {
            calls.Add("second");
            completion.SetResult();
            return Task.CompletedTask;
        });

        delays.CompleteAll();
        await completion.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Equal(["second"], calls);
    }

    // Проверяет, что Cancel не дает ожидающей задаче выполниться.
    [Fact]
    public void Cancel_prevents_pending_action_from_running()
    {
        var delays = new ManualDelayScheduler();
        var invoked = false;
        var debouncedAction = new DebouncedAsyncAction(
            TimeSpan.FromMilliseconds(25),
            null,
            delays.DelayAsync);

        debouncedAction.Schedule(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });
        debouncedAction.Cancel();
        delays.CompleteAll();

        Assert.False(invoked);
    }

    // Проверяет передачу исключений из action в обработчик ошибок.
    [Fact]
    public async Task Schedule_reports_action_failures_to_error_handler()
    {
        var delays = new ManualDelayScheduler();
        var expected = new InvalidOperationException("boom");
        var completion = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var debouncedAction = new DebouncedAsyncAction(
            TimeSpan.Zero,
            exception =>
            {
                completion.SetResult(exception);
                return Task.CompletedTask;
            },
            delays.DelayAsync);

        debouncedAction.Schedule(_ => Task.FromException(expected));

        delays.CompleteNext();
        var actual = await completion.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Same(expected, actual);
    }
}
