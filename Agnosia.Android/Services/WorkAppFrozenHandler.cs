using Agnosia.Android.Api.Vpn;
using Agnosia.Models;
using Android.Content;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Services;

internal static class WorkAppFrozenHandler
{
    public static async Task<OperationResult> RestoreParentVpnAndHideOverlayAsync(
        Context context,
        string trigger,
        string logTag,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await AndroidVpnAutomationApi.EnableConfiguredVpnAfterWorkFreezeAsync(context, trigger);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally
        {
            HideOverlay(context, logTag);
        }
    }

    private static void HideOverlay(Context context, string logTag)
    {
        try
        {
            OverlayVpnService.HideOverlay(context);
        }
        catch (Exception exception)
        {
            Log.Warn(logTag, $"Failed to hide overlay after work-app frozen: {exception.Message}");
        }
    }
}
