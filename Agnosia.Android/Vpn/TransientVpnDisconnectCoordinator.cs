using Agnosia.Models;
using Android.Content;
using Android.Net;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Vpn;

internal static class TransientVpnDisconnectCoordinator
{
    private const string LogTag = "AgnosiaTransientVpn";

    public static async Task<OperationResult> DisconnectActiveVpnAsync(
        AndroidActivityCommandGateway activityCommands,
        CancellationToken cancellationToken = default)
    {
        var activity = activityCommands.CurrentActivity;
        Intent? prepareIntent;
        try
        {
            prepareIntent = VpnService.Prepare(activity);
        }
        catch (Exception exception) when (AndroidRecoverableException.IsMatch(exception))
        {
            Log.Warn(LogTag, $"Failed to prepare VPN permission request before transient disconnect: {exception}");
            return OperationResult.Failure("Android не смог открыть запрос доступа к VPN.");
        }

        if (prepareIntent is null)
        {
            return await activityCommands.DisconnectPreparedVpnAsync(cancellationToken).ConfigureAwait(false);
        }
        
        var prepareResult =
            await activityCommands.StartExternalActivityForResultAsync(prepareIntent, cancellationToken)
                .ConfigureAwait(false);

        if (prepareResult.ResultCode != Result.Ok)
        {
            return OperationResult.Failure("Android не выдал Agnosia временное управление VPN.");
        }

        return await activityCommands.DisconnectPreparedVpnAsync(cancellationToken).ConfigureAwait(false);
    }
}
