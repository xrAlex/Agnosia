using Agnosia.Android.Vpn;
using Agnosia.Models;
using Xunit;

namespace Agnosia.Unit.Android.Vpn;

public sealed class VpnRestoreOwnershipCoordinatorTests
{
    [Fact]
    public async Task Restart_aborts_pending_launch_without_restore_obligation_without_querying_work_profile()
    {
        var pendingOwner = new VpnRestoreOwner("launch-a", "com.example.a");
        var storage = new InMemoryOwnershipStorage(
            VpnRestoreOwnershipState.Empty.Begin(pendingOwner));
        var coordinator = storage.CreateCoordinator(() => "launch-b");
        var launchCalled = false;

        var result = await coordinator.ExecuteLaunchAsync(
            "com.example.b",
            (_, _) =>
            {
                launchCalled = true;
                return Task.FromResult(OperationResult.Success("launched"));
            },
            () => Task.FromResult(OperationResult.Success("restored")),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.True(launchCalled);
        Assert.Equal(VpnRestoreOwnershipState.Empty, storage.State);
    }

    [Fact]
    public async Task Restart_restores_hidden_pending_owner_before_accepting_the_next_launch()
    {
        var pendingOwner = new VpnRestoreOwner("launch-a", "com.example.a");
        var storage = new InMemoryOwnershipStorage(
            VpnRestoreOwnershipState.Empty.Begin(pendingOwner).RequireRestore());
        var calls = new List<string>();
        var coordinator = storage.CreateCoordinator(
            () => "launch-b",
            (owner, _) =>
            {
                calls.Add($"query:{owner.LaunchId}");
                return Task.FromResult(VpnRestorePackageStateResult.Success(
                    installed: true,
                    hidden: true,
                    launchId: owner.LaunchId));
            });

        var result = await coordinator.ExecuteLaunchAsync(
            "com.example.b",
            (_, _) =>
            {
                calls.Add("launch:launch-b");
                return Task.FromResult(OperationResult.Success("launched"));
            },
            () =>
            {
                calls.Add("restore");
                return Task.FromResult(OperationResult.Success("restored"));
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(["query:launch-a", "restore", "launch:launch-b"], calls);
        Assert.Equal(VpnRestoreOwnershipState.Empty, storage.State);
    }

    [Fact]
    public async Task Restart_promotes_visible_pending_owner_only_when_launch_identity_matches()
    {
        var pendingOwner = new VpnRestoreOwner("launch-a", "com.example.a");
        var storage = new InMemoryOwnershipStorage(
            VpnRestoreOwnershipState.Empty.Begin(pendingOwner).RequireRestore());
        var coordinator = storage.CreateCoordinator(
            () => "launch-b",
            (owner, _) => Task.FromResult(VpnRestorePackageStateResult.Success(
                installed: true,
                hidden: false,
                launchId: owner.LaunchId)));
        var inherited = false;

        var result = await coordinator.ExecuteLaunchAsync(
            "com.example.b",
            (scope, _) =>
            {
                inherited = scope.HasInheritedRestoreObligation;
                return Task.FromResult(OperationResult.Success("launched"));
            },
            () => Task.FromResult(OperationResult.Success("restored")),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.True(inherited);
        Assert.Equal(new VpnRestoreOwner("launch-b", "com.example.b"), storage.State.ActiveOwner);
        Assert.Null(storage.State.PendingOwner);
    }

    [Fact]
    public async Task Restart_keeps_visible_pending_owner_when_launch_identity_is_unknown_or_different()
    {
        var pendingOwner = new VpnRestoreOwner("launch-a", "com.example.a");
        var storage = new InMemoryOwnershipStorage(
            VpnRestoreOwnershipState.Empty.Begin(pendingOwner).RequireRestore());
        var coordinator = storage.CreateCoordinator(
            () => "launch-b",
            (_, _) => Task.FromResult(VpnRestorePackageStateResult.Success(
                installed: true,
                hidden: false,
                launchId: "launch-other")));
        var launchCalled = false;

        var result = await coordinator.ExecuteLaunchAsync(
            "com.example.b",
            (_, _) =>
            {
                launchCalled = true;
                return Task.FromResult(OperationResult.Success("launched"));
            },
            () => Task.FromResult(OperationResult.Success("restored")),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.False(launchCalled);
        Assert.Equal(pendingOwner, storage.State.PendingOwner);
    }

    [Fact]
    public async Task Recovery_retries_failed_restore_for_a_hidden_active_owner()
    {
        var owner = new VpnRestoreOwner("launch-a", "com.example.a");
        var storage = new InMemoryOwnershipStorage(OwnedBy(owner.LaunchId, owner.PackageName));
        var coordinator = storage.CreateCoordinator(
            () => "unused",
            (_, _) => Task.FromResult(VpnRestorePackageStateResult.Success(
                installed: true,
                hidden: true,
                launchId: owner.LaunchId)));
        var restoreCalls = 0;

        var first = await coordinator.RecoverAsync(
            () =>
            {
                restoreCalls++;
                return Task.FromResult(OperationResult.Failure("restore failed"));
            },
            TestContext.Current.CancellationToken);
        var second = await coordinator.RecoverAsync(
            () =>
            {
                restoreCalls++;
                return Task.FromResult(OperationResult.Success("restored"));
            },
            TestContext.Current.CancellationToken);

        Assert.False(first.Result.Succeeded);
        Assert.False(first.RestoreSucceeded);
        Assert.True(second.Result.Succeeded);
        Assert.True(second.RestoreSucceeded);
        Assert.Equal(2, restoreCalls);
        Assert.Equal(VpnRestoreOwnershipState.Empty, storage.State);
    }

    [Fact]
    public async Task Failed_callback_restore_persists_ready_state_for_a_background_retry()
    {
        var owner = new VpnRestoreOwner("launch-a", "com.example.a");
        var storage = new InMemoryOwnershipStorage(OwnedBy(owner.LaunchId, owner.PackageName));
        var coordinator = storage.CreateCoordinator(() => "unused");

        var completion = await coordinator.CompleteOwnerAsync(
            owner.PackageName,
            owner.LaunchId,
            () => Task.FromResult(OperationResult.Failure("restore failed")),
            TestContext.Current.CancellationToken);

        Assert.True(completion.OwnerMatched);
        Assert.False(completion.Result.Succeeded);
        Assert.True(storage.State.RestoreReady);
        Assert.Equal(owner, storage.State.ActiveOwner);
    }

    [Fact]
    public async Task Recovery_retries_ready_owner_without_querying_work_profile()
    {
        var owner = new VpnRestoreOwner("launch-a", "com.example.a");
        var readyState = OwnedBy(owner.LaunchId, owner.PackageName)
            .MarkRestoreReady(owner.PackageName, owner.LaunchId);
        var storage = new InMemoryOwnershipStorage(readyState);
        var restoreCalls = 0;
        var coordinator = storage.CreateCoordinator(() => "unused");

        var recovery = await coordinator.RecoverAsync(
            () =>
            {
                restoreCalls++;
                return Task.FromResult(OperationResult.Success("restored"));
            },
            TestContext.Current.CancellationToken);

        Assert.True(recovery.Result.Succeeded);
        Assert.True(recovery.RestoreSucceeded);
        Assert.Equal(1, restoreCalls);
        Assert.Equal(VpnRestoreOwnershipState.Empty, storage.State);
    }

    [Fact]
    public async Task Interrupted_relaunch_of_same_package_preserves_previous_visible_owner()
    {
        var previous = new VpnRestoreOwner("old", "com.example.a");
        var pending = new VpnRestoreOwner("interrupted", previous.PackageName);
        var storage = new InMemoryOwnershipStorage(OwnedBy(previous.LaunchId, previous.PackageName).Begin(pending));
        var coordinator = storage.CreateCoordinator(() => "next",
            (_, _) => Task.FromResult(VpnRestorePackageStateResult.Success(true, false, previous.LaunchId)));
        var result = await coordinator.ExecuteLaunchAsync(previous.PackageName,
            (_, _) => Task.FromResult(OperationResult.Success("launched")),
            () => throw new InvalidOperationException("Previous work session is still visible"),
            TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded);
        Assert.Equal("next", storage.State.ActiveOwner?.LaunchId);
        Assert.Null(storage.State.PendingOwner);
    }

    [Fact]
    public async Task Durable_callback_acceptance_survives_restart_before_vpn_automation()
    {
        var owner = new VpnRestoreOwner("launch-a", "com.example.a");
        var storage = new InMemoryOwnershipStorage(OwnedBy(owner.LaunchId, owner.PackageName));
        var accepted = await storage.CreateCoordinator(() => "unused")
            .AcceptCompletionAsync(owner.PackageName, owner.LaunchId, TestContext.Current.CancellationToken);
        Assert.True(accepted.OwnerMatched);
        Assert.True(storage.State.RestoreReady);
        var restored = await storage.CreateCoordinator(() => "unused").RecoverAsync(
            () => Task.FromResult(OperationResult.Success("restored")), TestContext.Current.CancellationToken);
        Assert.True(restored.RestoreSucceeded);
    }

    [Fact]
    public async Task Delayed_boot_recovery_does_not_restore_over_a_new_work_launch()
    {
        var storage = new InMemoryOwnershipStorage(OwnedBy("new", "com.example.a"));
        var result = await storage.CreateCoordinator(() => "unused").RecoverAfterDeviceRestartAsync(
            new VpnRestoreOwner("old", "com.example.a"),
            () => throw new InvalidOperationException("Must not restore over a new launch"),
            TestContext.Current.CancellationToken);
        Assert.False(result.RestoreSucceeded);
        Assert.Equal("new", storage.State.ActiveOwner?.LaunchId);
    }

    [Fact]
    public async Task Device_restart_restores_a_pending_takeover_without_querying_work_profile()
    {
        var owner = new VpnRestoreOwner("launch-a", "com.example.a");
        var storage = new InMemoryOwnershipStorage(
            VpnRestoreOwnershipState.Empty.Begin(owner).RequireRestore());
        var coordinator = storage.CreateCoordinator(() => "unused");

        var recovery = await coordinator.RecoverAfterDeviceRestartAsync(
            owner,
            () => Task.FromResult(OperationResult.Success("restored")),
            TestContext.Current.CancellationToken);

        Assert.True(recovery.Result.Succeeded);
        Assert.True(recovery.RestoreSucceeded);
        Assert.Equal(VpnRestoreOwnershipState.Empty, storage.State);
    }

    [Fact]
    public async Task Recovery_restores_hidden_owner_when_work_process_lost_launch_identity()
    {
        var storage = new InMemoryOwnershipStorage(OwnedBy("launch-a", "com.example.a"));
        var coordinator = storage.CreateCoordinator(
            () => "unused",
            (_, _) => Task.FromResult(VpnRestorePackageStateResult.Success(
                installed: true,
                hidden: true,
                launchId: null)));
        var restoreCalls = 0;

        var recovery = await coordinator.RecoverAsync(
            () =>
            {
                restoreCalls++;
                return Task.FromResult(OperationResult.Success("restored"));
            },
            TestContext.Current.CancellationToken);

        Assert.True(recovery.RestoreSucceeded);
        Assert.Equal(1, restoreCalls);
        Assert.Equal(VpnRestoreOwnershipState.Empty, storage.State);
    }

    [Fact]
    public async Task Recovery_does_not_restore_a_visible_owner_or_a_mismatched_hidden_launch()
    {
        var snapshots = new Queue<VpnRestorePackageStateResult>(
        [
            VpnRestorePackageStateResult.Success(true, false, "launch-a"),
            VpnRestorePackageStateResult.Success(true, true, "launch-other")
        ]);
        var storage = new InMemoryOwnershipStorage(OwnedBy("launch-a", "com.example.a"));
        var coordinator = storage.CreateCoordinator(
            () => "unused",
            (_, _) => Task.FromResult(snapshots.Dequeue()));
        var restoreCalls = 0;

        var visible = await coordinator.RecoverAsync(Restore, TestContext.Current.CancellationToken);
        var mismatched = await coordinator.RecoverAsync(Restore, TestContext.Current.CancellationToken);

        Assert.True(visible.Result.Succeeded);
        Assert.False(visible.RestoreSucceeded);
        Assert.False(mismatched.Result.Succeeded);
        Assert.False(mismatched.RestoreSucceeded);
        Assert.Equal(0, restoreCalls);
        Assert.Equal("launch-a", storage.State.ActiveOwner?.LaunchId);
        return;

        Task<OperationResult> Restore()
        {
            restoreCalls++;
            return Task.FromResult(OperationResult.Success("restored"));
        }
    }

    [Fact]
    public async Task Recovery_does_not_repeat_vpn_automation_when_only_work_acknowledgement_failed()
    {
        var owner = new VpnRestoreOwner("launch-a", "com.example.a");
        var storage = new InMemoryOwnershipStorage(OwnedBy(owner.LaunchId, owner.PackageName));
        var confirmCalls = 0;
        var coordinator = storage.CreateCoordinator(
            () => "unused",
            (_, _) => Task.FromResult(VpnRestorePackageStateResult.Success(
                installed: true,
                hidden: true,
                launchId: owner.LaunchId)),
            (_, _) =>
            {
                confirmCalls++;
                return Task.FromResult(OperationResult.Failure("confirmation failed"));
            });

        var recovery = await coordinator.RecoverAsync(
            () => Task.FromResult(OperationResult.Success("restored")),
            TestContext.Current.CancellationToken);

        Assert.True(recovery.Result.Succeeded);
        Assert.True(recovery.RestoreSucceeded);
        Assert.Equal(1, confirmCalls);
        Assert.Equal(VpnRestoreOwnershipState.Empty, storage.State);
        var duplicate = await coordinator.CompleteOwnerAsync(owner.PackageName, owner.LaunchId,
            () => throw new InvalidOperationException("VPN must not toggle a second time"),
            TestContext.Current.CancellationToken);
        Assert.True(duplicate.Result.Succeeded);
        Assert.False(duplicate.OwnerMatched);
    }

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
        Assert.Equal("launch-a", storage.State.ActiveOwner?.LaunchId);
        Assert.True(storage.State.RestoreReady);
        Assert.True(storage.State.ForceRestore);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unconfirmed_launch_reconciles_without_rollback(bool queryAvailable)
    {
        var storage = new InMemoryOwnershipStorage();
        var restores = 0;
        var coordinator = storage.CreateCoordinator(() => "launch-new", (_, _) => Task.FromResult(
            queryAvailable ? VpnRestorePackageStateResult.Success(true, false, "launch-new")
                : VpnRestorePackageStateResult.Failure("profile unavailable")));
        var result = await coordinator.ExecuteLaunchAsync("target", (scope, _) =>
        {
            scope.MarkRestoreRequired();
            throw new Agnosia.Android.Commands.WorkLaunchUnconfirmedException("launch-new", "target");
        }, () => { restores++; return Task.FromResult(OperationResult.Success("restored")); }, CancellationToken.None);
        Assert.Equal(0, restores);
        Assert.True(storage.State.RestoreRequired);
        Assert.Equal("launch-new", (queryAvailable ? storage.State.ActiveOwner : storage.State.PendingOwner)?.LaunchId);
        Assert.Equal(queryAvailable, result.Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("other-launch")]
    public async Task Dispatched_launch_does_not_restore_from_hidden_package_without_matching_identity(string? observedId)
    {
        var pending = new VpnRestoreOwner("launch", "target");
        var storage = new InMemoryOwnershipStorage(VpnRestoreOwnershipState.Empty.Begin(pending).RequireRestore()
            with { PendingLaunchDispatched = true });
        var restores = 0;
        var coordinator = storage.CreateCoordinator(() => "unused", (_, _) => Task.FromResult(
            VpnRestorePackageStateResult.Success(true, true, observedId)));
        var recovery = await coordinator.RecoverAsync(() =>
        {
            restores++;
            return Task.FromResult(OperationResult.Success("restored"));
        }, TestContext.Current.CancellationToken);
        Assert.False(recovery.RestoreSucceeded);
        Assert.Equal(0, restores);
        Assert.Equal(pending, storage.State.PendingOwner);
    }

    [Fact]
    public async Task Matching_completion_of_unconfirmed_launch_restores_once_after_process_restart()
    {
        var pending = new VpnRestoreOwner("launch", "target");
        var storage = new InMemoryOwnershipStorage(VpnRestoreOwnershipState.Empty.Begin(pending).RequireRestore()
            with { PendingLaunchDispatched = true });
        var coordinator = storage.CreateCoordinator(() => "unused");
        var accepted = await coordinator.AcceptCompletionAsync("target", "launch", TestContext.Current.CancellationToken);
        Assert.True(accepted.OwnerMatched);
        Assert.True(storage.State.RestoreReady);
        var restarted = storage.CreateCoordinator(() => "unused");
        var restores = 0;
        Task<OperationResult> Restore()
        {
            restores++;
            return Task.FromResult(OperationResult.Success("restored"));
        }
        Assert.True((await restarted.RecoverAsync(Restore, TestContext.Current.CancellationToken)).RestoreSucceeded);
        Assert.False((await restarted.CompleteOwnerAsync("target", "launch", Restore, TestContext.Current.CancellationToken)).OwnerMatched);
        Assert.Equal(1, restores);
    }

    [Fact]
    public async Task Failed_restore_after_matching_hidden_reconciliation_retains_ready_owner()
    {
        var pending = new VpnRestoreOwner("launch", "target");
        var storage = new InMemoryOwnershipStorage(VpnRestoreOwnershipState.Empty.Begin(pending).RequireRestore()
            with { PendingLaunchDispatched = true });
        var coordinator = storage.CreateCoordinator(() => "unused", (_, _) => Task.FromResult(
            VpnRestorePackageStateResult.Success(true, true, "launch")));
        await coordinator.RecoverAsync(() => Task.FromResult(OperationResult.Failure("VPN not detected")), TestContext.Current.CancellationToken);
        Assert.True(storage.State.RestoreReady);
        Assert.Equal(pending, storage.State.ActiveOwner);
    }

    [Fact]
    public async Task Retry_for_old_owner_does_not_restore_current_owner()
    {
        var storage = new InMemoryOwnershipStorage(OwnedBy("current", "target") with { RestoreReady = true });
        var coordinator = storage.CreateCoordinator(() => "unused");
        var recovery = await coordinator.RecoverAsync(() => throw new InvalidOperationException("must not restore"),
            TestContext.Current.CancellationToken,
            expectedOwner: new VpnRestoreOwner("old", "target"));
        Assert.False(recovery.RestoreSucceeded);
        Assert.Equal("current", storage.State.ActiveOwner?.LaunchId);
    }

    [Fact]
    public async Task Missing_package_keeps_rollback_owner_after_failed_vpn_connection()
    {
        var pending = new VpnRestoreOwner("launch", "target");
        var storage = new InMemoryOwnershipStorage(VpnRestoreOwnershipState.Empty.Begin(pending).RequireRestore()
            with { PendingLaunchDispatched = true });
        var coordinator = storage.CreateCoordinator(() => "unused", (_, _) => Task.FromResult(
            VpnRestorePackageStateResult.Success(false, true, null)));
        await coordinator.RecoverAsync(() => Task.FromResult(OperationResult.Failure("no connection")),
            TestContext.Current.CancellationToken, pending);
        Assert.Equal(pending, storage.State.ActiveOwner);
        Assert.True(storage.State.RestoreReady);
        Assert.True(storage.State.ForceRestore);
        var recovery = await coordinator.RecoverAsync(() => Task.FromResult(OperationResult.Success("connected")),
            TestContext.Current.CancellationToken, pending);
        Assert.True(recovery.RestoreSucceeded);
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

        public VpnRestoreOwnershipCoordinator CreateCoordinator(
            Func<string> createLaunchId,
            Func<VpnRestoreOwner, CancellationToken, Task<VpnRestorePackageStateResult>>? queryPackageState = null,
            Func<VpnRestoreOwner, CancellationToken, Task<OperationResult>>? confirmRecovery = null)
        {
            return new VpnRestoreOwnershipCoordinator(
                () => _raw,
                raw => _raw = raw,
                () => _raw = null,
                () => LegacyFlag,
                () => LegacyFlag = false,
                createLaunchId,
                queryPackageState,
                confirmRecovery);
        }
    }
}
