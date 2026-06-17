using Agnosia.Android.Api.Platform;
using Agnosia.Android.Gateways;
using Xunit;

namespace Agnosia.Unit.AndroidApi.Gateways;

public sealed class AndroidQueryCacheTests
{
    [Fact]
    public void Boolean_cache_returns_value_until_ttl_expires()
    {
        var now = new ManualClock(new DateTimeOffset(2026, 6, 17, 10, 0, 0, TimeSpan.Zero));
        var cache = new AndroidQueryCache(now.GetUtcNow);

        cache.SetBoolean("query", true);

        Assert.True(cache.TryGetBoolean("query", out var cached));
        Assert.True(cached);

        now.Advance(TimeSpan.FromSeconds(6));

        Assert.False(cache.TryGetBoolean("query", out _));
    }

    [Fact]
    public void Owner_check_cache_stores_only_successful_results_and_can_be_cleared()
    {
        var now = new ManualClock(new DateTimeOffset(2026, 6, 17, 10, 0, 0, TimeSpan.Zero));
        var cache = new AndroidQueryCache(now.GetUtcNow);
        var failed = new WorkProfileOwnerCheckResult(WorkProfileOwnerCheckKind.Unreachable, "failed");
        var successful = new WorkProfileOwnerCheckResult(WorkProfileOwnerCheckKind.AppIsProfileOwner, "ok");

        cache.StoreOwnerCheckIfSuccessful(failed);

        Assert.False(cache.TryGetSuccessfulOwnerCheck(out _));

        cache.StoreOwnerCheckIfSuccessful(successful);

        Assert.True(cache.TryGetSuccessfulOwnerCheck(out var cached));
        Assert.Equal(successful, cached);

        cache.ClearOwnerCheck();

        Assert.False(cache.TryGetSuccessfulOwnerCheck(out _));
    }

    [Fact]
    public void Owner_check_cache_expires_successful_results()
    {
        var now = new ManualClock(new DateTimeOffset(2026, 6, 17, 10, 0, 0, TimeSpan.Zero));
        var cache = new AndroidQueryCache(now.GetUtcNow);

        cache.StoreOwnerCheckIfSuccessful(
            new WorkProfileOwnerCheckResult(WorkProfileOwnerCheckKind.AppIsProfileOwner, "ok"));

        now.Advance(TimeSpan.FromSeconds(4));

        Assert.False(cache.TryGetSuccessfulOwnerCheck(out _));
    }

    [Fact]
    public void Inventory_cache_returns_entry_only_for_matching_page_token_and_flags()
    {
        var now = new ManualClock(new DateTimeOffset(2026, 6, 17, 10, 0, 0, TimeSpan.Zero));
        var cache = new AndroidQueryCache(now.GetUtcNow);
        var query = new AndroidQueryCache.AppInventoryQuery(
            ShowAll: true,
            RiskEngineEnabled: false,
            Apps: [Model("org.example.app")],
            InteractionPackages: ["org.example.app"]);

        cache.StoreAppInventoryQuery("page-token", query);

        Assert.True(cache.TryGetAppInventoryQuery("page-token", showAll: true, riskEngineEnabled: false, out var cached));
        Assert.Equal(query, cached);
        Assert.False(cache.TryGetAppInventoryQuery("page-token", showAll: false, riskEngineEnabled: false, out _));
        Assert.False(cache.TryGetAppInventoryQuery("missing", showAll: true, riskEngineEnabled: false, out _));
        Assert.False(cache.TryGetAppInventoryQuery(null, showAll: true, riskEngineEnabled: false, out _));
    }

    [Fact]
    public void Inventory_cache_prunes_expired_entries_and_clear_removes_sessions()
    {
        var now = new ManualClock(new DateTimeOffset(2026, 6, 17, 10, 0, 0, TimeSpan.Zero));
        var cache = new AndroidQueryCache(now.GetUtcNow);
        var query = new AndroidQueryCache.AppInventoryQuery(
            ShowAll: false,
            RiskEngineEnabled: true,
            Apps: [Model("org.example.app")],
            InteractionPackages: []);

        cache.StoreAppInventoryQuery("page-token", query);
        now.Advance(TimeSpan.FromSeconds(61));

        Assert.False(cache.TryGetAppInventoryQuery("page-token", showAll: false, riskEngineEnabled: true, out _));

        cache.StoreAppInventoryQuery("page-token", query);
        cache.ClearAppInventoryQueries();

        Assert.False(cache.TryGetAppInventoryQuery("page-token", showAll: false, riskEngineEnabled: true, out _));
    }

    private static AppServiceModel Model(string packageName)
    {
        return new AppServiceModel
        {
            PackageName = packageName,
            Label = packageName,
            IsInstalled = true
        };
    }

    private sealed class ManualClock(DateTimeOffset value)
    {
        private DateTimeOffset _value = value;

        public DateTimeOffset GetUtcNow()
        {
            return _value;
        }

        public void Advance(TimeSpan timeSpan)
        {
            _value += timeSpan;
        }
    }
}
