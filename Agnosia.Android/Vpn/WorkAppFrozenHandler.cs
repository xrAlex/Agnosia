using Agnosia.Models;
using Android.Content;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Vpn;

internal static class WorkAppFrozenHandler
{
    public static async Task<OperationResult> RestoreParentVpnAndHideOverlayAsync(
        Context context,
        string packageName,
        string? launchId,
        string trigger,
        string logTag,
        CancellationToken cancellationToken = default)
    {
        var ownerMatched = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var coordinator = ServiceRegistry.GetRequiredService<VpnRestoreOwnershipCoordinator>();
            var completion = await coordinator.CompleteOwnerAsync(
                    packageName,
                    launchId,
                    () => AndroidVpnAutomationApi.EnableConfiguredVpnAfterWorkFreezeAsync(context, trigger),
                    cancellationToken)
                .ConfigureAwait(false);
            ownerMatched = completion.OwnerMatched;
            cancellationToken.ThrowIfCancellationRequested();
            return completion.Result;
        }
        finally
        {
            if (ownerMatched) HideOverlay(context, logTag);
        }
    }

    public static async Task<OperationResult> RollbackFailedWorkLaunchAsync(
        Context context,
        string trigger,
        string logTag)
    {
        try
        {
            return await AndroidVpnAutomationApi.RestoreConfiguredVpnAfterFailedWorkLaunchAsync(context, trigger)
                .ConfigureAwait(false);
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
