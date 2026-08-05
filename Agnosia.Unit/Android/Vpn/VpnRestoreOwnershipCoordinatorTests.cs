using Agnosia.Android.Vpn;
using Agnosia.Models;
using Xunit;

namespace Agnosia.Unit.Android.Vpn;

public sealed class VpnRestoreOwnershipCoordinatorTests
{
    [Fact]
    public async Task Failed_inherited_launch_keeps_first_owner_and_skips_restore()
    {
        var storage = new InMemoryOwnershipStorage(OwnedBy("launch-a", "com.example.a"));
        var coordinator = storage.CreateCoordinator(() => "launch-b");
        var restoreCalls = 0;

        var result = await coordinator.ExecuteLaunchAsync(
            "com.example.b",
            (scope, _) =>
            {
                Assert.True(scope.HasInheritedRestoreObligation);
                return Task.FromResult(OperationResult.Failure("launch failed"));
            },
            () =>
            {
                restoreCalls++;
                return Task.FromResult(OperationResult.Success("restored"));
            },
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, restoreCalls);
        Assert.Equal("launch-a", storage.State.ActiveOwner?.LaunchId);
        Assert.Null(storage.State.PendingOwner);
    }

    [Fact]
    public async Task Successful_first_launch_commits_owner_after_claiming_restore()
    {
        var storage = new InMemoryOwnershipStorage();
        var coordinator = storage.CreateCoordinator(() => "launch-a");

        var result = await coordinator.ExecuteLaunchAsync(
            "com.example.a",
            (scope, _) =>
            {
                Assert.False(scope.HasInheritedRestoreObligation);
                scope.MarkRestoreRequired();
                return Task.FromResult(OperationResult.Success("launched"));
            },
            () => Task.FromResult(OperationResult.Success("restored")),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(storage.State.RestoreRequired);
        Assert.Equal(new VpnRestoreOwner("launch-a", "com.example.a"), storage.State.ActiveOwner);
        Assert.Null(storage.State.PendingOwner);
    }

    [Fact]
    public async Task Successful_second_launch_atomically_replaces_first_owner()
    {
        var storage = new InMemoryOwnershipStorage(OwnedBy("launch-a", "com.example.a"));
        var coordinator = storage.CreateCoordinator(() => "launch-b");

        var result = await coordinator.ExecuteLaunchAsync(
            "com.example.b",
            (scope, _) => Task.FromResult(OperationResult.Success(scope.LaunchId)),
            () => Task.FromResult(OperationResult.Success("restored")),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("launch-b", storage.State.ActiveOwner?.LaunchId);
        Assert.Equal("com.example.b", storage.State.ActiveOwner?.PackageName);
        Assert.Null(storage.State.PendingOwner);
    }

    [Fact]
    public async Task Successful_first_launch_rollback_clears_obligation()
    {
        var storage = new InMemoryOwnershipStorage();
        var coordinator = storage.CreateCoordinator(() => "launch-a");

        var result = await coordinator.ExecuteLaunchAsync(
            "com.example.a",
            async (scope, _) =>
            {
                scope.MarkRestoreRequired();
                var rollback = await scope.RollbackAsync();
                Assert.True(rollback.Succeeded);
                return OperationResult.Failure("launch failed");
            },
            () => Task.FromResult(OperationResult.Success("restored")),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(VpnRestoreOwnershipState.Empty, storage.State);
    }

    [Fact]
    public async Task Failed_first_launch_rollback_keeps_restore_obligation()
    {
        var storage = new InMemoryOwnershipStorage();
        var coordinator = storage.CreateCoordinator(() => "launch-a");

        var result = await coordinator.ExecuteLaunchAsync(
            "com.example.a",
            async (scope, _) =>
            {
                scope.MarkRestoreRequired();
                var rollback = await scope.RollbackAsync();
                Assert.False(rollback.Succeeded);
                return OperationResult.Failure("launch failed");
            },
            () => Task.FromResult(OperationResult.Failure("restore failed")),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(storage.State.RestoreRequired);
        Assert.Null(storage.State.ActiveOwner);
        Assert.Null(storage.State.PendingOwner);
    }

    [Fact]
    public async Task Stale_callback_does_not_restore_or_clear_current_owner()
    {
        var storage = new InMemoryOwnershipStorage(OwnedBy("launch-b", "com.example.same"));
        var coordinator = storage.CreateCoordinator(() => "unused");
        var restoreCalls = 0;

        var completion = await coordinator.CompleteOwnerAsync(
            "com.example.same",
            "launch-a",
            () =>
            {
                restoreCalls++;
                return Task.FromResult(OperationResult.Success("restored"));
            },
            TestContext.Current.CancellationToken);

        Assert.False(completion.OwnerMatched);
        Assert.True(completion.Result.Succeeded);
        Assert.Equal(0, restoreCalls);
        Assert.Equal("launch-b", storage.State.ActiveOwner?.LaunchId);
    }

    [Fact]
    public async Task Current_callback_restores_once_and_clears_owner()
    {
        var storage = new InMemoryOwnershipStorage(OwnedBy("launch-b", "com.example.b"));
        var coordinator = storage.CreateCoordinator(() => "unused");
        var restoreCalls = 0;

        var first = await coordinator.CompleteOwnerAsync(
            "com.example.b",
            "launch-b",
            Restore,
            TestContext.Current.CancellationToken);
        var second = await coordinator.CompleteOwnerAsync(
            "com.example.b",
            "launch-b",
            Restore,
            TestContext.Current.CancellationToken);

        Assert.True(first.OwnerMatched);
        Assert.True(first.Result.Succeeded);
        Assert.False(second.OwnerMatched);
        Assert.Equal(1, restoreCalls);
        Assert.Equal(VpnRestoreOwnershipState.Empty, storage.State);
        return;

        Task<OperationResult> Restore()
        {
            restoreCalls++;
            return Task.FromResult(OperationResult.Success("restored"));
        }
    }

    [Fact]
    public async Task Failed_current_callback_restore_preserves_owner_for_retry()
    {
        var storage = new InMemoryOwnershipStorage(OwnedBy("launch-a", "com.example.a"));
        var coordinator = storage.CreateCoordinator(() => "unused");

        var completion = await coordinator.CompleteOwnerAsync(
            "com.example.a",
            "launch-a",
            () => Task.FromResult(OperationResult.Failure("restore failed")),
            TestContext.Current.CancellationToken);

        Assert.True(completion.OwnerMatched);
        Assert.False(completion.Result.Succeeded);
        Assert.Equal("launch-a", storage.State.ActiveOwner?.LaunchId);
    }

    [Fact]
    public async Task Legacy_boolean_is_migrated_and_legacy_callback_can_complete_it()
    {
        var storage = new InMemoryOwnershipStorage { LegacyFlag = true };
        var coordinator = storage.CreateCoordinator(() => "unused");

        var completion = await coordinator.CompleteOwnerAsync(
            "com.example.legacy",
            null,
            () => Task.FromResult(OperationResult.Success("restored")),
            TestContext.Current.CancellationToken);

        Assert.True(completion.OwnerMatched);
        Assert.True(completion.Result.Succeeded);
        Assert.False(storage.LegacyFlag);
        Assert.Equal(VpnRestoreOwnershipState.Empty, storage.State);
    }

    [Fact]
    public async Task Concurrent_launches_are_serialized_until_first_commit()
    {
        var storage = new InMemoryOwnershipStorage();
        var launchIds = new Queue<string>(["launch-a", "launch-b"]);
        var coordinator = storage.CreateCoordinator(launchIds.Dequeue);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = false;

        var first = coordinator.ExecuteLaunchAsync(
            "com.example.a",
            async (scope, _) =>
            {
                scope.MarkRestoreRequired();
                firstStarted.SetResult();
                await releaseFirst.Task;
                return OperationResult.Success("first");
            },
            () => Task.FromResult(OperationResult.Success("restored")),
            CancellationToken.None);
        await firstStarted.Task;

        var second = coordinator.ExecuteLaunchAsync(
            "com.example.b",
            (_, _) =>
            {
                secondStarted = true;
                return Task.FromResult(OperationResult.Success("second"));
            },
            () => Task.FromResult(OperationResult.Success("restored")),
            CancellationToken.None);

        await Task.Yield();
        Assert.False(secondStarted);

        releaseFirst.SetResult();
        await Task.WhenAll(first, second);

        Assert.True(secondStarted);
        Assert.Equal("launch-b", storage.State.ActiveOwner?.LaunchId);
    }

    private static VpnRestoreOwnershipState OwnedBy(string launchId, string packageName)
    {
        var owner = new VpnRestoreOwner(launchId, packageName);
        return VpnRestoreOwnershipState.Empty.Begin(owner).RequireRestore().Commit(owner);
    }

    private sealed class InMemoryOwnershipStorage(VpnRestoreOwnershipState? initial = null)
    {
        private string? _raw = initial is null ? null : VpnRestoreOwnershipCodec.Serialize(initial);

        public bool LegacyFlag { get; set; }

        public VpnRestoreOwnershipState State =>
            VpnRestoreOwnershipCodec.TryDeserialize(_raw, out var state)
                ? state
                : VpnRestoreOwnershipState.Empty;

        public VpnRestoreOwnershipCoordinator CreateCoordinator(Func<string> createLaunchId)
        {
            return new VpnRestoreOwnershipCoordinator(
                () => _raw,
                raw => _raw = raw,
                () => _raw = null,
                () => LegacyFlag,
                () => LegacyFlag = false,
                createLaunchId);
        }
    }
}
