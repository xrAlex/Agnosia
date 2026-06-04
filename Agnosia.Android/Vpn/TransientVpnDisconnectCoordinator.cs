using Agnosia.Models;
using Android.App;
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
        if (prepareIntent is not null)
        {
            var prepareResult =
                await activityCommands.StartExternalActivityForResultAsync(prepareIntent, cancellationToken);
            if (prepareResult.ResultCode != Result.Ok)
                return OperationResult.Failure("Android не выдал Agnosia временное управление VPN.");
        }

        return await activityCommands.DisconnectPreparedVpnAsync(cancellationToken);
    }
}
