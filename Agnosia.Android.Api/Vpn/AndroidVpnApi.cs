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
            var activeNetwork = connectivityManager.ActiveNetwork;
            if (HasVpnTransport(connectivityManager, activeNetwork))
            {
                Log.Debug(LogTag, $"VPN detected via ActiveNetwork: {activeNetwork}.");
                return true;
            }

            Log.Debug(LogTag, "No VPN detected on the current default network.");
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
