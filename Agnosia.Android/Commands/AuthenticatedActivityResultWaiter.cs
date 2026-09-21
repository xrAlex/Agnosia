namespace Agnosia.Android.Commands;

internal static class AuthenticatedActivityResultWaiter
{
    public static async Task<T> WaitAsync<T>(
        Task<T> activityResult,
        Task<T> authenticatedCallback,
        Func<T, bool> authenticateActivityResult,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The callback can win before a delayed Activity task fails.
        _ = activityResult.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        var first = await Task.WhenAny(activityResult, authenticatedCallback).WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (authenticatedCallback.IsCompletedSuccessfully)
            return await authenticatedCallback.ConfigureAwait(false);
        if (first == activityResult)
        {
            var result = await activityResult.ConfigureAwait(false);
            if (authenticateActivityResult(result)) return result;
        }
        return await authenticatedCallback.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
