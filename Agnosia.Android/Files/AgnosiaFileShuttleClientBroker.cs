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

    public static void Preconnect(Context context)
    {
        GetClient(context).Preconnect();
    }
}
