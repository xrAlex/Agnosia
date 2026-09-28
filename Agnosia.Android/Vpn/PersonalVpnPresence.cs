using Agnosia.Android.Api.Vpn;
using Agnosia.Android.Storage;
using Android.Content;
using Android.Net;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Vpn;

internal static class PersonalVpnPresence
{
    private const string LogTag = "AgnosiaVpnAutomation";

    public static bool IsActive(Context context)
    {
        if (!AndroidVpnApi.IsVpnActive(context)) return false;
        if (!LockdownSettingsStore.IsEnabled()) return true;

        var visibleVpnCount = AndroidVpnApi.GetVisibleVpnNetworkHandles(context).Count;
        if (visibleVpnCount != 1 || AndroidVpnApi.IsVpnActiveOnDefaultNetwork(context)) return true;

        try
        {
            var preparationRequired = VpnService.Prepare(context) is not null;
            if (!VpnPresencePolicy.IsOnlyWorkLockdownVpn(
                    lockdownEnabled: true,
                    visibleVpnCount: visibleVpnCount,
                    defaultNetworkIsVpn: false,
                    personalVpnPreparationRequired: preparationRequired))
                return true;

            Log.Debug(LogTag, "Ignoring the work-profile Lockdown VPN during personal VPN detection.");
            return false;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Could not identify the visible VPN profile: {exception.Message}");
            return true;
        }
    }
}
