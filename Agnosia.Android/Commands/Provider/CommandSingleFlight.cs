namespace Agnosia.Android.Commands;

internal sealed class CommandSingleFlight<T>
{
    private readonly Lock _sync = new();
    private Task<T>? _pending;

    public Task<T> RunAsync(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_pending is null || _pending.IsCompleted)
                _pending = Task.Run(operation);
            return _pending.WaitAsync(cancellationToken);
        }
    }
}
