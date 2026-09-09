namespace Agnosia.Android.Files;

internal sealed class AgnosiaFileShuttleServiceLifetime
{
    private readonly Lock _sync = new();
    private int _activeOperations;
    private int _generation;
    private bool _idleStopPending;

    public int RegisterActivity()
    {
        lock (_sync)
        {
            _idleStopPending = false;
            return ++_generation;
        }
    }

    public void BeginOperation()
    {
        lock (_sync)
        {
            _activeOperations++;
        }
    }

    public bool CompleteOperation()
    {
        lock (_sync)
        {
            if (_activeOperations == 0) return false;

            _activeOperations--;
            if (_activeOperations > 0 || !_idleStopPending) return false;

            _idleStopPending = false;
            return true;
        }
    }

    public bool RequestIdleStop(int generation)
    {
        lock (_sync)
        {
            if (generation != _generation) return false;
            if (_activeOperations == 0) return true;

            _idleStopPending = true;
            return false;
        }
    }
}
