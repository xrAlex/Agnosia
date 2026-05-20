namespace Agnosia.Unit.TestSupport;

internal sealed class ManualDelayScheduler
{
    private readonly Lock _sync = new();
    private readonly Queue<DelayRequest> _requests = [];
    private readonly List<TimeSpan> _requestedDelays = [];

    public IReadOnlyList<TimeSpan> RequestedDelays
    {
        get
        {
            lock (_sync)
            {
                return _requestedDelays.ToArray();
            }
        }
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        var request = new DelayRequest(cancellationToken);
        request.RegisterCancellation();

        lock (_sync)
        {
            _requestedDelays.Add(delay);
            _requests.Enqueue(request);
        }

        return request.Task;
    }

    public void CompleteNext()
    {
        var request = DequeueNextIncompleteRequest();
        if (request is null)
            throw new InvalidOperationException("No pending delay request is available.");

        request.Complete();
    }

    public void CompleteAll()
    {
        while (true)
        {
            var request = DequeueNextIncompleteRequest();
            if (request is null) return;

            request.Complete();
        }
    }

    private DelayRequest? DequeueNextIncompleteRequest()
    {
        lock (_sync)
        {
            while (_requests.Count > 0)
            {
                var request = _requests.Dequeue();
                if (!request.IsCompleted) return request;
            }
        }

        return null;
    }

    private sealed class DelayRequest : IDisposable
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly CancellationToken _cancellationToken;
        private CancellationTokenRegistration _cancellationRegistration;

        public DelayRequest(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
        }

        public Task Task => _completion.Task;

        public bool IsCompleted => _completion.Task.IsCompleted;

        public void RegisterCancellation()
        {
            if (_cancellationToken.CanBeCanceled)
                _cancellationRegistration = _cancellationToken.Register(
                    static state => ((DelayRequest)state!).Cancel(),
                    this);
        }

        public void Complete()
        {
            if (_completion.TrySetResult()) Dispose();
        }

        public void Cancel()
        {
            if (_completion.TrySetCanceled(_cancellationToken)) Dispose();
        }

        public void Dispose()
        {
            _cancellationRegistration.Dispose();
        }
    }
}
