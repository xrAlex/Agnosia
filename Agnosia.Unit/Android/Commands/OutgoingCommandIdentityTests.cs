using Agnosia.Android.Api.Commands;
using Agnosia.Android.Commands;
using Xunit;

namespace Agnosia.Unit.Android.Commands;

public sealed class OutgoingCommandIdentityTests
{
    [Theory]
    [InlineData(AgnosiaActions.StartFileShuttleParentToWork, AndroidCommandKind.StartFileShuttleParentToWork)]
    [InlineData(AgnosiaActions.StartFileShuttleWorkToParent, AndroidCommandKind.StartFileShuttleWorkToParent)]
    [InlineData(AgnosiaActions.SynchronizePreference, AndroidCommandKind.SynchronizePreference)]
    [InlineData(AgnosiaActions.FreezePackage, AndroidCommandKind.FreezePackage)]
    public void Resolve_CreatesIdentityForLegacySenders(string action, object expected)
    {
        var identity = AndroidCommandIntentMapper.ResolveOutgoingIdentity(action, null, null);
        Assert.NotEqual(Guid.Empty, identity.CorrelationId);
        Assert.Equal(expected, identity.Kind);
    }

    [Fact]
    public void Resolve_PreservesExistingCorrelationAndRejectsMismatchedKind()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id, AndroidCommandIntentMapper.ResolveOutgoingIdentity(
            AgnosiaActions.FreezePackage, id.ToString(), nameof(AndroidCommandKind.FreezePackage)).CorrelationId);
        Assert.Throws<InvalidOperationException>(() => AndroidCommandIntentMapper.ResolveOutgoingIdentity(
            AgnosiaActions.FreezePackage, id.ToString(), nameof(AndroidCommandKind.UnfreezePackage)));
    }
}
