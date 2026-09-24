namespace Agnosia.Services;

/// <summary>Shares reads of one state generation and combines invalidations into one follow-up refresh.</summary>
public sealed class CoalescingRefreshWorker
{
    private readonly Lock _sync = new();
    private TaskCompletionSource? _running;
    private long _generation;
    private long _activeGeneration;
    private bool _activeIncludesPermissions;
    private bool _includePermissions;

    public Task RunAsync(Func<bool, Task> refresh, bool stateChanged = false, bool includePermissions = true)
    {
        lock (_sync)
        {
            if (_running is null) _includePermissions = includePermissions;
            else if (stateChanged)
            {
                var hasPending = _generation != _activeGeneration || (_includePermissions && !_activeIncludesPermissions);
                _includePermissions = includePermissions || (hasPending && _includePermissions);
            }
            else _includePermissions |= includePermissions;
            if (stateChanged) _generation++;
            if (_running is not null) return _running.Task;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _running = completion;
            _ = Task.Run(() => RunLoopAsync(refresh, completion));
            return completion.Task;
        }
    }

    private async Task RunLoopAsync(Func<bool, Task> refresh, TaskCompletionSource completion)
    {
        try
        {
            while (true)
            {
                long generation;
                bool permissions;
                lock (_sync)
                {
                    generation = _activeGeneration = _generation;
                    permissions = _activeIncludesPermissions = _includePermissions;
                }
                await refresh(permissions).ConfigureAwait(false);
                lock (_sync)
                {
                    if (_generation != generation || (!permissions && _includePermissions)) continue;
                    _running = null;
                    _includePermissions = false;
                    completion.SetResult();
                    return;
                }
            }
        }
        catch (Exception exception)
        {
            lock (_sync) { _running = null; _includePermissions = false; completion.SetException(exception); }
        }
    }
}
