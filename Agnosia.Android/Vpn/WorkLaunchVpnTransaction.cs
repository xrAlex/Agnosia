using Agnosia.Models;

namespace Agnosia.Android.Vpn;

internal readonly record struct WorkLaunchVpnTakeoverResult(
    OperationResult Result,
    bool RollbackRequired)
{
    public static WorkLaunchVpnTakeoverResult Acquired(OperationResult result)
    {
        return new WorkLaunchVpnTakeoverResult(result, true);
    }

    public static WorkLaunchVpnTakeoverResult NotRequired(OperationResult result)
    {
        return new WorkLaunchVpnTakeoverResult(result, false);
    }
}

internal static class WorkLaunchVpnTransaction
{
    public static async Task<OperationResult> ExecuteAsync(
        Func<CancellationToken, Task<OperationResult>> preflight,
        Func<CancellationToken, Task<WorkLaunchVpnTakeoverResult>> takeover,
        Func<CancellationToken, Task<OperationResult>> launch,
        Func<Task<OperationResult>> rollback,
        Func<bool> rollbackRequiredOnException,
        CancellationToken cancellationToken)
    {
        var preflightResult = await preflight(cancellationToken).ConfigureAwait(false);
        if (!preflightResult.Succeeded) return preflightResult;

        var rollbackRequired = false;
        try
        {
            var takeoverResult = await takeover(cancellationToken).ConfigureAwait(false);
            rollbackRequired = takeoverResult.RollbackRequired;
            if (!takeoverResult.Result.Succeeded)
            {
                return rollbackRequired
                    ? await RollBackFailureAsync(takeoverResult.Result, rollback).ConfigureAwait(false)
                    : takeoverResult.Result;
            }

            var launchResult = await launch(cancellationToken).ConfigureAwait(false);
            if (launchResult.Succeeded || !rollbackRequired) return launchResult;

            return await RollBackFailureAsync(launchResult, rollback).ConfigureAwait(false);
        }
        catch
        {
            if (rollbackRequired || rollbackRequiredOnException())
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
