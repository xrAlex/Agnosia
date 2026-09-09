using Agnosia.Android.Files;
using Xunit;

namespace Agnosia.Unit.Android.Files;

public sealed class AgnosiaFileShuttleServiceLifetimeTests
{
    // Ловит остановку сервиса по таймеру, срок которого был сброшен более новым запросом.
    [Fact]
    public void RequestIdleStop_ignores_stale_generation()
    {
        var lifetime = new AgnosiaFileShuttleServiceLifetime();
        var staleGeneration = lifetime.RegisterActivity();

        _ = lifetime.RegisterActivity();

        Assert.False(lifetime.RequestIdleStop(staleGeneration));
    }

    // Ловит обрыв выполняющейся операции, когда таймер простоя срабатывает во время неё.
    [Fact]
    public void RequestIdleStop_defers_until_active_operation_completes()
    {
        var lifetime = new AgnosiaFileShuttleServiceLifetime();
        var generation = lifetime.RegisterActivity();
        lifetime.BeginOperation();

        Assert.False(lifetime.RequestIdleStop(generation));
        Assert.True(lifetime.CompleteOperation());
        Assert.False(lifetime.CompleteOperation());
    }

    // Ловит отложенную остановку, которая не была отменена новой активностью.
    [Fact]
    public void RegisterActivity_cancels_a_pending_idle_stop()
    {
        var lifetime = new AgnosiaFileShuttleServiceLifetime();
        var generation = lifetime.RegisterActivity();
        lifetime.BeginOperation();
        Assert.False(lifetime.RequestIdleStop(generation));

        _ = lifetime.RegisterActivity();

        Assert.False(lifetime.CompleteOperation());
    }
}
