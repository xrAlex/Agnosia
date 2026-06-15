namespace Agnosia.Services;

public sealed class DebouncedAsyncAction
{
    private readonly TimeSpan _delay;
    private readonly Func<Exception, Task>? _onError;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private CancellationTokenSource? _pendingCancellation;

    public DebouncedAsyncAction(TimeSpan delay, Func<Exception, Task>? onError = null)
        : this(delay, onError, Task.Delay)
    {
    }

    internal DebouncedAsyncAction(
        TimeSpan delay,
        Func<Exception, Task>? onError,
        Func<TimeSpan, CancellationToken, Task> delayAsync)
    {
        if (delay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "Debounce delay cannot be negative.");

        _delay = delay;
        _onError = onError;
        _delayAsync = delayAsync ?? throw new ArgumentNullException(nameof(delayAsync));
    }

    public void Schedule(Func<CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Cancel();

        var cancellation = new CancellationTokenSource();
        _pendingCancellation = cancellation;
        _ = RunAsync(cancellation, action);
    }

    public void Cancel()
    {
        _pendingCancellation?.Cancel();
        _pendingCancellation = null;
    }

    private async Task RunAsync(CancellationTokenSource cancellation, Func<CancellationToken, Task> action)
    {
        try
        {
            await _delayAsync(_delay, cancellation.Token).ConfigureAwait(false);
            await action(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (_onError is not null) await _onError(exception).ConfigureAwait(false);
        }
        finally
        {
            if (ReferenceEquals(_pendingCancellation, cancellation)) _pendingCancellation = null;

            cancellation.Dispose();
        }
    }
}
