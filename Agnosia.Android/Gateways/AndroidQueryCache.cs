using Agnosia.Android.Api.Platform;

namespace Agnosia.Android.Gateways;

internal sealed class AndroidQueryCache
{
    private static readonly TimeSpan BooleanCacheTtl = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OwnerCheckSuccessTtl = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan AppInventoryQueryTtl = TimeSpan.FromSeconds(60);

    private readonly Func<DateTimeOffset> _getUtcNow;
    private readonly Lock _sync = new();
    private readonly Dictionary<string, CachedBooleanQuery> _booleanQueries = [];
    private readonly Dictionary<string, CachedAppInventoryQuery> _appInventoryQueries = [];
    private WorkProfileOwnerCheckResult? _cachedOwnerCheckSuccess;
    private DateTimeOffset _ownerCheckExpiresAtUtc;

    public static AndroidQueryCache Shared { get; } = new();

    public AndroidQueryCache()
        : this(() => DateTimeOffset.UtcNow)
    {
    }

    internal AndroidQueryCache(Func<DateTimeOffset> getUtcNow)
    {
        _getUtcNow = getUtcNow;
    }

    public bool TryGetSuccessfulOwnerCheck(out WorkProfileOwnerCheckResult result)
    {
        lock (_sync)
        {
            if (_cachedOwnerCheckSuccess is not null && _getUtcNow() <= _ownerCheckExpiresAtUtc)
            {
                result = _cachedOwnerCheckSuccess;
                return true;
            }

            _cachedOwnerCheckSuccess = null;
            result = null!;
            return false;
        }
    }

    public void StoreOwnerCheckIfSuccessful(WorkProfileOwnerCheckResult result)
    {
        if (result.Kind != WorkProfileOwnerCheckKind.AppIsProfileOwner) return;

        lock (_sync)
        {
            _cachedOwnerCheckSuccess = result;
            _ownerCheckExpiresAtUtc = _getUtcNow() + OwnerCheckSuccessTtl;
        }
    }

    public void ClearOwnerCheck()
    {
        lock (_sync)
        {
            _cachedOwnerCheckSuccess = null;
            _ownerCheckExpiresAtUtc = default;
        }
    }

    public bool TryGetBoolean(string action, out bool value)
    {
        lock (_sync)
        {
            if (_booleanQueries.TryGetValue(action, out var cached)
                && _getUtcNow() - cached.CachedAt <= BooleanCacheTtl)
            {
                value = cached.Value;
                return true;
            }

            _booleanQueries.Remove(action);
        }

        value = false;
        return false;
    }

    public void SetBoolean(string action, bool value)
    {
        lock (_sync)
        {
            _booleanQueries[action] = new CachedBooleanQuery(value, _getUtcNow());
        }
    }

    public void ClearBoolean(string? action = null)
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(action))
                _booleanQueries.Clear();
            else
                _booleanQueries.Remove(action);
        }
    }

    public bool TryGetAppInventoryQuery(
        string? pageToken,
        bool showAll,
        bool riskEngineEnabled,
        out AppInventoryQuery query)
    {
        query = null!;
        if (string.IsNullOrWhiteSpace(pageToken)) return false;

        var now = _getUtcNow();
        lock (_sync)
        {
            PruneExpiredAppInventoryQueries(now);
            if (!_appInventoryQueries.TryGetValue(pageToken, out var cached)
                || cached.Query.ShowAll != showAll
                || cached.Query.RiskEngineEnabled != riskEngineEnabled)
            {
                _appInventoryQueries.Remove(pageToken);
                return false;
            }

            query = cached.Query;
            return true;
        }
    }

    public void StoreAppInventoryQuery(string? pageToken, AppInventoryQuery query)
    {
        if (string.IsNullOrWhiteSpace(pageToken)) return;

        var now = _getUtcNow();
        lock (_sync)
        {
            PruneExpiredAppInventoryQueries(now);
            _appInventoryQueries[pageToken] = new CachedAppInventoryQuery(query, now);
        }
    }

    public void ClearAppInventoryQueries()
    {
        lock (_sync)
        {
            _appInventoryQueries.Clear();
        }
    }

    private void PruneExpiredAppInventoryQueries(DateTimeOffset now)
    {
        foreach (var (key, query) in _appInventoryQueries.ToArray())
            if (now - query.CachedAt > AppInventoryQueryTtl)
                _appInventoryQueries.Remove(key);
    }

    public sealed record AppInventoryQuery(
        bool ShowAll,
        bool RiskEngineEnabled,
        IReadOnlyList<AppServiceModel> Apps,
        string[] InteractionPackages);

    private sealed record CachedBooleanQuery(bool Value, DateTimeOffset CachedAt);

    private sealed record CachedAppInventoryQuery(AppInventoryQuery Query, DateTimeOffset CachedAt);
}
