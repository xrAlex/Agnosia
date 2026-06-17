using Android.Content;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Gateways;

internal static class AndroidProfileCommandTransport
{
    private const string LogTag = "AgnosiaProfileCommand";

    public static async Task<Intent?> StartForDataAsync(
        AndroidActivityCommandGateway commandRunner,
        Intent intent,
        string failureLogMessage,
        CancellationToken cancellationToken)
    {
        var result = await commandRunner.StartActivityForResultAsync(
            intent,
            true,
            cancellationToken).ConfigureAwait(false);
        if (TryGetResultData(result, out var data)) return data;

        Log.Warn(LogTag, failureLogMessage);
        return null;
    }

    private static bool TryGetResultData(AndroidActivityResult result, out Intent data)
    {
        if (result.ResultCode == Result.Ok && result.Data is { } resultData)
        {
            data = resultData;
            return true;
        }

        data = null!;
        return false;
    }
}
