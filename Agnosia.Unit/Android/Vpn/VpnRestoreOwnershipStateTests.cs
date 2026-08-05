using Agnosia.Android.Vpn;
using Xunit;

namespace Agnosia.Unit.Android.Vpn;

public sealed class VpnRestoreOwnershipStateTests
{
    private static readonly VpnRestoreOwner OwnerA = new("launch-a", "com.example.a");
    private static readonly VpnRestoreOwner OwnerB = new("launch-b", "com.example.b");

    [Fact]
    public void Commit_second_owner_replaces_first_without_clearing_restore_obligation()
    {
        var state = VpnRestoreOwnershipState.Empty
            .Begin(OwnerA)
            .RequireRestore()
            .Commit(OwnerA)
            .Begin(OwnerB)
            .Commit(OwnerB);

        Assert.True(state.RestoreRequired);
        Assert.Equal(OwnerB, state.ActiveOwner);
        Assert.Null(state.PendingOwner);
    }

    [Fact]
    public void Abort_second_owner_preserves_first_owner()
    {
        var owned = VpnRestoreOwnershipState.Empty
            .Begin(OwnerA)
            .RequireRestore()
            .Commit(OwnerA);

        var aborted = owned.Begin(OwnerB).Abort(OwnerB);

        Assert.True(aborted.RestoreRequired);
        Assert.Equal(OwnerA, aborted.ActiveOwner);
        Assert.Null(aborted.PendingOwner);
    }

    [Fact]
    public void Callback_matches_launch_identity_not_only_package()
    {
        var oldOwner = new VpnRestoreOwner("launch-old", "com.example.same");
        var currentOwner = new VpnRestoreOwner("launch-new", "com.example.same");
        var current = VpnRestoreOwnershipState.Empty
            .Begin(currentOwner)
            .RequireRestore()
            .Commit(currentOwner);

        Assert.False(current.MatchesCallback(oldOwner.PackageName, oldOwner.LaunchId));
        Assert.True(current.MatchesCallback(currentOwner.PackageName, currentOwner.LaunchId));
    }

    [Fact]
    public void Begin_rejects_a_second_simultaneous_pending_owner()
    {
        var state = VpnRestoreOwnershipState.Empty.Begin(OwnerA);

        Assert.Throws<InvalidOperationException>(() => state.Begin(OwnerB));
    }

    [Fact]
    public void ClearAfterRestore_removes_the_entire_obligation()
    {
        var owned = VpnRestoreOwnershipState.Empty
            .Begin(OwnerA)
            .RequireRestore()
            .Commit(OwnerA);

        var cleared = owned.ClearAfterRestore();

        Assert.Equal(VpnRestoreOwnershipState.Empty, cleared);
    }

    [Fact]
    public void Legacy_callback_matches_only_legacy_ownership()
    {
        Assert.True(VpnRestoreOwnershipState.Legacy.MatchesCallback("com.example.legacy", null));

        var current = VpnRestoreOwnershipState.Empty
            .Begin(OwnerA)
            .RequireRestore()
            .Commit(OwnerA);
        Assert.False(current.MatchesCallback(OwnerA.PackageName, null));
    }

    [Theory]
    [InlineData("", "com.example.app")]
    [InlineData("launch-id", "")]
    [InlineData(" ", "com.example.app")]
    [InlineData("launch-id", " ")]
    public void Owner_rejects_blank_identity(string launchId, string packageName)
    {
        Assert.Throws<ArgumentException>(() => new VpnRestoreOwner(launchId, packageName));
    }
}
