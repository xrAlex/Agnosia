namespace Agnosia.Android.Gateways;

internal static class AndroidProfileBooleanQueryCache
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(5);
    private static readonly Lock CacheSync = new();
    private static readonly Dictionary<string, CachedBooleanQuery> Cache = [];

    public static bool TryGet(string action, out bool value)
    {
        lock (CacheSync)
        {
            if (Cache.TryGetValue(action, out var cached)
                && DateTimeOffset.UtcNow - cached.CachedAt <= CacheTtl)
            {
                value = cached.Value;
                return true;
            }
        }

        value = false;
        return false;
    }

    public static void Set(string action, bool value)
    {
        lock (CacheSync)
        {
            Cache[action] = new CachedBooleanQuery(value, DateTimeOffset.UtcNow);
        }
    }

    private sealed record CachedBooleanQuery(bool Value, DateTimeOffset CachedAt);
}
