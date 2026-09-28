using Agnosia.Android.Vpn;
using Xunit;

namespace Agnosia.Unit.Android.Vpn;

public sealed class VpnPresencePolicyTests
{
    [Theory]
    [InlineData(true, 1, false, false, true)]
    [InlineData(false, 1, false, false, false)]
    [InlineData(true, 0, false, false, false)]
    [InlineData(true, 2, false, false, false)]
    [InlineData(true, 1, true, false, false)]
    [InlineData(true, 1, false, true, false)]
    public void Only_work_lockdown_vpn_is_ignored_without_hiding_personal_vpn(
        bool lockdownEnabled,
        int visibleVpnCount,
        bool defaultNetworkIsVpn,
        bool personalVpnPreparationRequired,
        bool expected)
    {
        Assert.Equal(expected, VpnPresencePolicy.IsOnlyWorkLockdownVpn(
            lockdownEnabled, visibleVpnCount, defaultNetworkIsVpn, personalVpnPreparationRequired));
    }
}
