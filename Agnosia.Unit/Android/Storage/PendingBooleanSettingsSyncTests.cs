using Agnosia.Android.Storage;
using Agnosia.Models;
using Xunit;

namespace Agnosia.Unit.Android.Storage;

public sealed class PendingBooleanSettingsSyncTests
{
    [Fact]
    public async Task FailedDisable_SurvivesNewCoordinatorAndRetriesUnchangedValue()
    {
        var store = new Dictionary<string, string?>();
        var first = Create(store);
        first.Queue("logging", false);
        Assert.False((await first.FlushAsync((_, _, _) => Task.FromResult(OperationResult.Failure("offline")), TestContext.Current.CancellationToken)).Succeeded);
        var restored = Create(store);
        bool? applied = null;
        Assert.True((await restored.FlushAsync((_, value, _) =>
        {
            applied = value;
            return Task.FromResult(OperationResult.Success("ack"));
        }, TestContext.Current.CancellationToken)).Succeeded);
        Assert.False(applied);
        Assert.Empty(store);
    }

    [Fact]
    public async Task OldAcknowledgement_DoesNotEraseNewerSetting()
    {
        var store = new Dictionary<string, string?>();
        var sync = Create(store);
        sync.Queue("logging", true);
        await sync.FlushAsync((_, _, _) =>
        {
            sync.Queue("logging", false);
            return Task.FromResult(OperationResult.Success("ack"));
        }, TestContext.Current.CancellationToken);
        Assert.NotEmpty(store);
        bool? applied = null;
        await sync.FlushAsync((_, value, _) =>
        {
            applied = value;
            return Task.FromResult(OperationResult.Success("ack"));
        }, TestContext.Current.CancellationToken);
        Assert.False(applied);
        Assert.Empty(store);
    }

    [Fact]
    public async Task SuccessfulAcknowledgement_WithFailedDurableRemoval_RemainsPendingForRetry()
    {
        var store = new Dictionary<string, string?>();
        var sync = new PendingBooleanSettingsSync(
            ["logging"],
            key => store.GetValueOrDefault(key),
            (key, value) => store[key] = value,
            _ => throw new IOException("disk unavailable"));
        sync.Queue("logging", false);

        var result = await sync.FlushAsync(
            (_, _, _) => Task.FromResult(OperationResult.Success("ack")),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.NotEmpty(store);
    }

    private static PendingBooleanSettingsSync Create(Dictionary<string, string?> store) => new(
        ["logging"], key => store.GetValueOrDefault(key), (key, value) => store[key] = value,
        key => store.Remove(key));
}
