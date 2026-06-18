using Agnosia.Models;
using Android.Content;

namespace Agnosia.Android.Vpn;

internal static class TransientVpnDisconnectService
{
    public static Task<OperationResult> DisconnectPreparedVpnAsync(
        Context context,
        CancellationToken cancellationToken = default)
    {
        return LockdownVpnService.DisconnectPreparedVpnAsync(context, cancellationToken);
    }
}
