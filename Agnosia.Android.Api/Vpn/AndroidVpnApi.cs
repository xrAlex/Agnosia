using Agnosia.Android.Api.Platform;
using Android.Content;
using Android.Net;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Api.Vpn;

public static class AndroidVpnApi
{
    private const string LogTag = "AgnosiaTransientVpn";
    private const int PerUserUidRange = 100000;
    private const string LockdownVpnSession = "Agnosia Lockdown";
    private const string TransientVpnSession = "Agnosia VPN Override";

    public static bool IsVpnActive(Context context)
    {
        return IsVpnActive(context, new HashSet<long>());
    }

    public static bool IsVpnActive(Context context, IReadOnlySet<long> ignoredVpnNetworkHandles)
    {
        if (AndroidSystemApi.GetConnectivityManager(context) is not { } connectivityManager) return false;

        try
        {
            var ownUid = context.ApplicationInfo?.Uid ?? -1;
            var activeNetwork = connectivityManager.ActiveNetwork;
            if (HasExternalVpnTransport(connectivityManager, activeNetwork, ownUid, ignoredVpnNetworkHandles))
            {
                Log.Debug(LogTag, $"VPN detected via ActiveNetwork: {activeNetwork}.");
                return true;
            }

#pragma warning disable CA1422
            // One-shot VPN Guard check; callbacks are not useful for this synchronous launch gate.
            foreach (var network in connectivityManager.GetAllNetworks())
#pragma warning restore CA1422
            {
                if (Equals(network, activeNetwork)
                    || !HasExternalVpnTransport(connectivityManager, network, ownUid, ignoredVpnNetworkHandles))
                    continue;

                Log.Debug(LogTag, $"VPN detected via network scan: {network}.");
                return true;
            }

            Log.Debug(LogTag, "No VPN detected on visible networks.");
            return false;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to determine VPN state: {exception.Message}");
            return false;
        }
    }

    public static IReadOnlySet<long> GetVisibleVpnNetworkHandles(Context context)
    {
        if (AndroidSystemApi.GetConnectivityManager(context) is not { } connectivityManager)
            return new HashSet<long>();

        try
        {
            var handles = new HashSet<long>();
#pragma warning disable CA1422
            // One-shot VPN Guard baseline before transient disconnect.
            foreach (var network in connectivityManager.GetAllNetworks())
#pragma warning restore CA1422
            {
                var capabilities = connectivityManager.GetNetworkCapabilities(network);
                if (capabilities?.HasTransport(TransportType.Vpn) == true)
                    handles.Add(network.NetworkHandle);
            }

            Log.Debug(LogTag, $"Visible VPN baseline captured. count={handles.Count}.");
            return handles;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to capture visible VPN baseline: {exception.Message}");
            return new HashSet<long>();
        }
    }

    private static bool HasExternalVpnTransport(
        ConnectivityManager connectivityManager,
        Network? network,
        int ownUid,
        IReadOnlySet<long> ignoredVpnNetworkHandles)
    {
        if (network is null) return false;

        var capabilities = connectivityManager.GetNetworkCapabilities(network);
        if (capabilities?.HasTransport(TransportType.Vpn) != true) return false;

        if (ignoredVpnNetworkHandles.Contains(network.NetworkHandle))
        {
            Log.Debug(LogTag, $"Ignoring baseline VPN network: {network}.");
            return false;
        }

        if (!IsAgnosiaVpnSession(capabilities, ownUid)) return true;

        Log.Debug(LogTag, $"Ignoring Agnosia VPN network: {network}, ownerUid={capabilities.OwnerUid}.");
        return false;
    }

    private static bool IsAgnosiaVpnSession(NetworkCapabilities capabilities, int ownUid)
    {
        var transportInfo = capabilities.TransportInfo?.ToString();
        return IsSameAppId(capabilities.OwnerUid, ownUid)
               || transportInfo?.Contains($"sessionId={LockdownVpnSession}", StringComparison.Ordinal) == true
               || transportInfo?.Contains($"sessionId={TransientVpnSession}", StringComparison.Ordinal) == true;
    }

    private static bool IsSameAppId(int candidateUid, int ownUid)
    {
        return candidateUid >= 0
               && ownUid >= 0
               && candidateUid % PerUserUidRange == ownUid % PerUserUidRange;
    }
}
