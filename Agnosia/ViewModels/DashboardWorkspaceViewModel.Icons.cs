using Agnosia.Models;

namespace Agnosia.ViewModels;

public partial class DashboardWorkspaceViewModel
{
    internal async Task<byte[]?> LoadAppIconPngAsync(AppSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (snapshot.IconPng is { Length: > 0 } existingIcon) return existingIcon;

        return await QueueIconLoadAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    private Task<byte[]?> QueueIconLoadAsync(AppSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<byte[]?>(cancellationToken);

        var pendingIconLoad = new PendingIconLoad(snapshot, cancellationToken);
        lock (_iconBatchProcessorSync)
        {
            _pendingIconLoads.Writer.TryWrite(pendingIconLoad);
            _iconBatchProcessor ??= ProcessIconLoadBatchesAsync();
        }

        return pendingIconLoad.Task;
    }

    private async Task ProcessIconLoadBatchesAsync()
    {
        while (true)
        {
            await _delayAsync(TimeSpan.FromMilliseconds(IconBatchDelayMs), CancellationToken.None)
                .ConfigureAwait(false);
            List<PendingIconLoad>? batch = null;
            lock (_iconBatchProcessorSync)
            {
                while ((batch?.Count ?? 0) < MaxIconBatchSize
                       && _pendingIconLoads.Reader.TryRead(out var request))
                {
                    if (request.IsCompleted)
                    {
                        request.Dispose();
                        continue;
                    }

                    batch ??= [];
                    batch.Add(request);
                }

                if (batch is null)
                {
                    _iconBatchProcessor = null;
                    return;
                }
            }

            await LoadIconBatchAsync(batch).ConfigureAwait(false);
        }
    }

    private async Task LoadIconBatchAsync(IReadOnlyList<PendingIconLoad> batch)
    {
        var snapshots = GetDistinctIconSnapshots(batch);

        IReadOnlyDictionary<AppItemKey, byte[]?> icons;
        await _iconLoadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            icons = await _dashboardService.LoadAppIconsAsync(snapshots).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            foreach (var request in batch) request.TrySetException(exception);

            return;
        }
        finally
        {
            _iconLoadGate.Release();
        }

        foreach (var request in batch)
        {
            icons.TryGetValue(AppItemKey.FromSnapshot(request.Snapshot), out var iconPng);
            request.TrySetResult(iconPng);
        }
    }

    private static AppSnapshot[] GetDistinctIconSnapshots(IReadOnlyList<PendingIconLoad> batch)
    {
        var snapshots = new List<AppSnapshot>(batch.Count);
        var seen = new HashSet<AppItemKey>();
        foreach (var t in batch)
        {
            var snapshot = t.Snapshot;
            if (seen.Add(AppItemKey.FromSnapshot(snapshot)))
                snapshots.Add(snapshot);
        }

        return snapshots.Count == 0 ? [] : snapshots.ToArray();
    }

    private sealed class PendingIconLoad : IDisposable
    {
        private readonly TaskCompletionSource<byte[]?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly CancellationToken _cancellationToken;
        private readonly CancellationTokenRegistration _cancellationRegistration;

        public PendingIconLoad(AppSnapshot snapshot, CancellationToken cancellationToken)
        {
            Snapshot = snapshot;
            _cancellationToken = cancellationToken;
            _cancellationRegistration = cancellationToken.CanBeCanceled
                ? cancellationToken.Register(static state =>
                    ((PendingIconLoad)state!)._completion.TrySetCanceled(
                        ((PendingIconLoad)state)._cancellationToken), this)
                : default;
        }

        public AppSnapshot Snapshot { get; }

        public Task<byte[]?> Task => _completion.Task;

        public bool IsCompleted => _completion.Task.IsCompleted;

        public void TrySetResult(byte[]? iconPng)
        {
            _completion.TrySetResult(iconPng);
            Dispose();
        }

        public void TrySetException(Exception exception)
        {
            _completion.TrySetException(exception);
            Dispose();
        }

        public void Dispose() => _cancellationRegistration.Dispose();
    }
}
