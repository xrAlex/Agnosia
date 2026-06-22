namespace Agnosia.Android.Commands;

internal sealed class AndroidCommandScheduler
{
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly Lock _refreshSync = new();
    private readonly Dictionary<RefreshKey, CancellationTokenSource> _activeRefreshCancellations = [];

    public async Task<T> RunAsync<T>(
        AndroidCommandEnvelope envelope,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (envelope.Priority == AndroidCommandPriority.Mutation)
            return await RunMutationAsync(action, cancellationToken).ConfigureAwait(false);

        if (envelope.Priority == AndroidCommandPriority.Refresh)
            return await RunRefreshAsync(envelope, action, cancellationToken).ConfigureAwait(false);

        return await action(cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> RunMutationAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task<T> RunRefreshAsync<T>(
        AndroidCommandEnvelope envelope,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        var key = new RefreshKey(envelope.Kind, envelope.TargetProfile);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationTokenSource? previous;
        lock (_refreshSync)
        {
            _activeRefreshCancellations.TryGetValue(key, out previous);
            _activeRefreshCancellations[key] = linkedCancellation;
        }

        TryCancelPreviousRefresh(previous);

        try
        {
            return await action(linkedCancellation.Token).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            throw new OperationCanceledException(linkedCancellation.Token);
        }
        finally
        {
            lock (_refreshSync)
            {
                if (_activeRefreshCancellations.TryGetValue(key, out var current)
                    && ReferenceEquals(current, linkedCancellation))
                    _activeRefreshCancellations.Remove(key);
            }
        }
    }

    private static void TryCancelPreviousRefresh(CancellationTokenSource? previous)
    {
        if (previous is null) return;

        try
        {
            previous.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (AggregateException)
        {
        }
    }

    private readonly record struct RefreshKey(
        AndroidCommandKind Kind,
        AndroidCommandTargetProfile TargetProfile);
}
