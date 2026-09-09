using Android.Content;

namespace Agnosia.Android.Files;

internal static class AgnosiaFileShuttleClientBroker
{
    private static readonly Lock Sync = new();
    private static AgnosiaFileShuttleMessengerClient? _client;

    public static AgnosiaFileShuttleMessengerClient GetClient(Context context)
    {
        lock (Sync)
        {
            return _client ??= new AgnosiaFileShuttleMessengerClient(context);
        }
    }

    public static Task PreconnectAsync(Context context, CancellationToken cancellationToken = default)
    {
        return GetClient(context).PreconnectAsync(cancellationToken);
    }

    public static void Disconnect()
    {
        AgnosiaFileShuttleMessengerClient? client;
        lock (Sync)
        {
            client = _client;
            _client = null;
        }

        client?.Close();
    }

    public static void Disconnect(AgnosiaFileShuttleMessengerClient client)
    {
        lock (Sync)
            if (ReferenceEquals(_client, client)) _client = null;
        client.Close();
    }
}
