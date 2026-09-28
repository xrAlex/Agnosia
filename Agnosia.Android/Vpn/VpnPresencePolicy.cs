namespace Agnosia.Android.Vpn;

internal static class VpnPresencePolicy
{
    public static bool IsOnlyWorkLockdownVpn(
        bool lockdownEnabled,
        int visibleVpnCount,
        bool defaultNetworkIsVpn,
        bool personalVpnPreparationRequired)
    {
        return lockdownEnabled
               && visibleVpnCount == 1
               && !defaultNetworkIsVpn
               && !personalVpnPreparationRequired;
    }
}
