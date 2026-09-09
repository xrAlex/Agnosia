using Agnosia.Android.Files;
using Xunit;

namespace Agnosia.Unit.Android.Files;

public sealed class AgnosiaFileShuttleClientRegistryTests
{
    // IPC creates a fresh wrapper on every request from the same client.
    [Fact]
    public void Drain_returns_one_callback_per_connection_after_repeated_requests()
    {
        var registry = new AgnosiaFileShuttleClientRegistry<object>();
        var latestCallback = new object();
        for (var index = 0; index < 50; index++) registry.Register("client-a", new object());
        registry.Register("client-a", latestCallback);

        Assert.Same(latestCallback, Assert.Single(registry.Drain()));
        Assert.Empty(registry.Drain());
    }

    [Fact]
    public void Drain_preserves_distinct_clients()
    {
        var registry = new AgnosiaFileShuttleClientRegistry<object>();
        var first = new object();
        var second = new object();
        registry.Register("client-a", first);
        registry.Register("client-b", second);

        Assert.Equal(new[] { first, second }, registry.Drain());
    }

    [Fact]
    public void Remove_prevents_notification_to_a_closed_client()
    {
        var registry = new AgnosiaFileShuttleClientRegistry<object>();
        registry.Register("client-a", new object());
        registry.Remove("client-a");

        Assert.Empty(registry.Drain());
    }
}
