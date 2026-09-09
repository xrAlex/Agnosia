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
        var restoreSucceeded = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var coordinator = ServiceRegistry.GetRequiredService<VpnRestoreOwnershipCoordinator>();
            var completion = await coordinator.CompleteOwnerAsync(
                    packageName,
                    launchId,
                    () => RestoreOwnedVpnAsync(context, trigger),
                    cancellationToken)
                .ConfigureAwait(false);
            restoreSucceeded = completion.OwnerMatched && completion.Result.Succeeded;
            cancellationToken.ThrowIfCancellationRequested();
            return completion.Result;
        }
        finally
        {
            if (restoreSucceeded) HideOverlay(context, logTag);
        }
    }

    public static async Task<OperationResult> RollbackFailedWorkLaunchAsync(
        Context context,
        string trigger,
        string logTag)
    {
        var result = await AndroidVpnAutomationApi.RestoreConfiguredVpnAfterFailedWorkLaunchAsync(context, trigger)
            .ConfigureAwait(false);
        if (result.Succeeded) HideOverlay(context, logTag);
        return result;
    }

    public static async Task<OperationResult> RecoverAfterDeviceRestartAndHideOverlayAsync(
        Context context,
        VpnRestoreOwner? expectedOwner,
        string trigger,
        string logTag,
        CancellationToken cancellationToken = default)
    {
        var coordinator = ServiceRegistry.GetRequiredService<VpnRestoreOwnershipCoordinator>();
        var recovery = await coordinator.RecoverAfterDeviceRestartAsync(
                expectedOwner,
                () => RestoreOwnedVpnAsync(context, trigger),
                cancellationToken)
            .ConfigureAwait(false);
        if (recovery.Result.Succeeded && recovery.RestoreSucceeded) HideOverlay(context, logTag);
        return recovery.Result;
    }

    internal static Task<OperationResult> RestoreOwnedVpnAsync(Context context, string trigger)
    {
        var storage = ServiceRegistry.GetRequiredService<LocalStorageManager>();
        var force = VpnRestoreOwnershipCodec.TryDeserialize(storage.GetString(StorageKeys.VpnRestoreOwnershipState),
            out var state) && state.ForceRestore;
        return force
            ? AndroidVpnAutomationApi.RestoreConfiguredVpnAfterFailedWorkLaunchAsync(context, trigger)
            : AndroidVpnAutomationApi.EnableConfiguredVpnAfterWorkFreezeAsync(context, trigger);
    }

    internal static void HideOverlay(Context context, string logTag)
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
