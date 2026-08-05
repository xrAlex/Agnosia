using Agnosia.Models;

namespace Agnosia.Android.Vpn;

internal static class WorkLaunchVpnTransaction
{
    public static async Task<OperationResult> ExecuteAsync(
        Func<CancellationToken, Task<OperationResult>> preflight,
        Func<CancellationToken, Task<OperationResult>> takeover,
        Func<CancellationToken, Task<OperationResult>> launch,
        Func<Task<OperationResult>> rollback,
        CancellationToken cancellationToken)
    {
        var preflightResult = await preflight(cancellationToken).ConfigureAwait(false);
        if (!preflightResult.Succeeded) return preflightResult;

        try
        {
            var takeoverResult = await takeover(cancellationToken).ConfigureAwait(false);
            if (!takeoverResult.Succeeded)
                return await RollBackFailureAsync(takeoverResult, rollback).ConfigureAwait(false);

            var launchResult = await launch(cancellationToken).ConfigureAwait(false);
            return launchResult.Succeeded
                ? launchResult
                : await RollBackFailureAsync(launchResult, rollback).ConfigureAwait(false);
        }
        catch
        {
            await TryRollbackAsync(rollback).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<OperationResult> RollBackFailureAsync(
        OperationResult failure,
        Func<Task<OperationResult>> rollback)
    {
        var rollbackResult = await TryRollbackAsync(rollback).ConfigureAwait(false);
        return rollbackResult.Succeeded
            ? failure
            : OperationResult.Failure(
                $"{failure.Message} Не удалось восстановить VPN: {rollbackResult.Message}");
    }

    private static async Task<OperationResult> TryRollbackAsync(Func<Task<OperationResult>> rollback)
    {
        try
        {
            return await rollback().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return OperationResult.Failure(exception.Message);
        }
    }
}
