using Agnosia.Android.Api.Platform;
using Android.Content;
using Android.Net;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Api.Vpn;

public static class AndroidVpnApi
{
    private const string LogTag = "AgnosiaTransientVpn";

    public static bool IsVpnActive(Context context)
    {
        if (AndroidSystemApi.GetConnectivityManager(context) is not { } connectivityManager) return false;

        try
        {
            if (HasVpnTransport(connectivityManager, connectivityManager.ActiveNetwork))
            {
                Log.Debug(LogTag, "VPN detected via ActiveNetwork.");
                return true;
            }

#pragma warning disable CA1422
            var networks = connectivityManager.GetAllNetworks();
#pragma warning restore CA1422
            foreach (var network in networks)
            {
                if (!HasVpnTransport(connectivityManager, network)) continue;

                Log.Debug(LogTag, $"VPN detected among available networks: {network}.");
                return true;
            }

            Log.Debug(LogTag, "No active VPN detected in ActiveNetwork or across all networks.");
            return false;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to determine VPN state: {exception.Message}");
            return false;
        }
    }

    private static bool HasVpnTransport(ConnectivityManager connectivityManager, Network? network)
    {
        return network is not null
               && connectivityManager.GetNetworkCapabilities(network)?.HasTransport(TransportType.Vpn) == true;
    }
}