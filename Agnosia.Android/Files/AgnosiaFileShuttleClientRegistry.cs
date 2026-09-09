namespace Agnosia.Android.Files;

internal sealed class AgnosiaFileShuttleClientRegistry<T>
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, T> _clients = new(StringComparer.Ordinal);

    public void Register(string connectionId, T callback)
    {
        lock (_sync) _clients[connectionId] = callback;
    }

    public void Remove(string connectionId)
    {
        lock (_sync) _clients.Remove(connectionId);
    }

    public T[] Drain()
    {
        lock (_sync)
        {
            var clients = _clients.Values.ToArray();
            _clients.Clear();
            return clients;
        }
    }
}
