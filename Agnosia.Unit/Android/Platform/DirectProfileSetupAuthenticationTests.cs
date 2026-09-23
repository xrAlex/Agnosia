using Agnosia.Android.Platform;
using Xunit;

namespace Agnosia.Unit.Android.Platform;

public sealed class DirectProfileSetupAuthenticationTests
{
    private static readonly string Key = new('A', 64);
    private static readonly string OtherKey = new('B', 64);

    [Fact]
    public void Accepts_initial_key_and_idempotent_replay_but_never_replaces_existing_key()
    {
        Assert.True(DirectProfileSetupAuthentication.CanAccept(null, Key, true, true, 12, 12));
        Assert.True(DirectProfileSetupAuthentication.CanAccept(Key, Key, true, true, 12, 12));
        Assert.False(DirectProfileSetupAuthentication.CanAccept(Key, OtherKey, true, true, 12, 12));
        Assert.False(DirectProfileSetupAuthentication.CanAccept("corrupt", Key, true, true, 12, 12));
    }

    [Theory]
    [InlineData(false, true, 12, 12)]
    [InlineData(true, false, 12, 12)]
    [InlineData(true, true, 0, 0)]
    [InlineData(true, true, 12, 13)]
    public void Rejects_wrong_profile_or_owner(bool managed, bool owner, int actual, int expected)
    {
        Assert.False(DirectProfileSetupAuthentication.CanAccept(null, Key, managed, owner, actual, expected));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a key")]
    public void Rejects_invalid_incoming_key(string? incoming)
    {
        Assert.False(DirectProfileSetupAuthentication.CanAccept(null, incoming, true, true, 12, 12));
    }
}
