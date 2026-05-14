namespace Agnosia.Services;

public sealed class DebouncedAsyncAction
{
    private readonly TimeSpan _delay;
    private readonly Func<Exception, Task>? _onError;
    private CancellationTokenSource? _pendingCancellation;

    public DebouncedAsyncAction(TimeSpan delay, Func<Exception, Task>? onError = null)
    {
        if (delay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "Debounce delay cannot be negative.");

        _delay = delay;
        _onError = onError;
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
            await Task.Delay(_delay, cancellation.Token);
            await action(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (_onError is not null) await _onError(exception);
        }
        finally
        {
            if (ReferenceEquals(_pendingCancellation, cancellation)) _pendingCancellation = null;

            cancellation.Dispose();
        }
    }
}