using Agnosia.Models;
using Android.Net;

namespace Agnosia.Android.Vpn;

internal static class TransientVpnDisconnectCoordinator
{
    public static async Task<OperationResult> DisconnectActiveVpnAsync(
        AndroidActivityCommandGateway activityCommands,
        CancellationToken cancellationToken = default)
    {
        var activity = activityCommands.CurrentActivity;
        var prepareIntent = VpnService.Prepare(activity);
        if (prepareIntent is null)
            return await activityCommands.DisconnectPreparedVpnAsync(cancellationToken).ConfigureAwait(false);
        
        var prepareResult =
            await activityCommands.StartExternalActivityForResultAsync(prepareIntent, cancellationToken)
                .ConfigureAwait(false);
        
        if (prepareResult.ResultCode != Result.Ok)
            return OperationResult.Failure("Android не выдал Agnosia временное управление VPN.");

        return await activityCommands.DisconnectPreparedVpnAsync(cancellationToken).ConfigureAwait(false);
    }
}
