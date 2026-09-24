namespace Agnosia.Android.Commands;

/// <summary>Cancellation abandons the waiter, never the slot owned by a running native call.</summary>
internal sealed class BoundedCommandExecutor(int concurrency)
{
    private readonly SemaphoreSlim _slots = new(concurrency, concurrency);
    private readonly SemaphoreSlim _ordered = new(1, 1);

    public async Task<T> RunAsync<T>(Func<T> call, CancellationToken cancellationToken, bool ordered = false)
    {
        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        var running = Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ordered) _ordered.Wait(cancellationToken);
                try { cancellationToken.ThrowIfCancellationRequested(); return call(); }
                finally { if (ordered) _ordered.Release(); }
            }
            finally { _slots.Release(); }
        }, CancellationToken.None);
        // Observe a late exception even when the caller has already stopped waiting.
        _ = running.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return await running.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
