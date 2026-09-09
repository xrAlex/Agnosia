using System.Collections.Concurrent;
using Agnosia.Models;

namespace Agnosia.Android.Commands;

internal sealed record WorkLaunchAcknowledgement(string LaunchId, string PackageName, bool Succeeded, string Message);

internal sealed class WorkLaunchAcknowledgementWaiter(string launchId, string packageName)
{
    private readonly TaskCompletionSource<WorkLaunchAcknowledgement> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<WorkLaunchAcknowledgement> Task => _completion.Task;

    public async Task<OperationResult> DispatchAndWaitAsync(Func<CancellationToken, Task<OperationResult>> dispatch,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            OperationResult sent;
            try { sent = await dispatch(cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OperationCanceledException && exception is not TimeoutException)
            {
                return OperationResult.Failure(exception.Message);
            }
            if (!sent.Succeeded) return sent;
            var acknowledgement = await Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new(acknowledgement.Succeeded, acknowledgement.Message);
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
        {
            throw new WorkLaunchUnconfirmedException(launchId, packageName);
        }
    }

    public bool TryAccept(WorkLaunchAcknowledgement acknowledgement) =>
        !string.IsNullOrWhiteSpace(acknowledgement.LaunchId)
        && !string.IsNullOrWhiteSpace(acknowledgement.PackageName)
        && acknowledgement.LaunchId == launchId && acknowledgement.PackageName == packageName
        && _completion.TrySetResult(acknowledgement);
}

internal static class WorkLaunchAcknowledgements
{
    private static readonly ConcurrentDictionary<string, WorkLaunchAcknowledgementWaiter> Pending = new();

    public static WorkLaunchAcknowledgementWaiter Register(string launchId, string packageName)
    {
        var waiter = new WorkLaunchAcknowledgementWaiter(launchId, packageName);
        if (!Pending.TryAdd(launchId, waiter)) throw new InvalidOperationException("Launch already registered.");
        return waiter;
    }

    public static bool Accept(WorkLaunchAcknowledgement acknowledgement) =>
        Pending.TryGetValue(acknowledgement.LaunchId, out var waiter) && waiter.TryAccept(acknowledgement);

    public static void Remove(string launchId) => Pending.TryRemove(launchId, out _);
}

// The command may have reached Android. Only reconciliation can decide whether to restore VPN.
internal sealed class WorkLaunchUnconfirmedException(string launchId, string packageName)
    : Exception("Результат запуска пока неизвестен. Agnosia проверит рабочую сессию перед восстановлением VPN.")
{
    public string LaunchId { get; } = launchId;
    public string PackageName { get; } = packageName;
}
