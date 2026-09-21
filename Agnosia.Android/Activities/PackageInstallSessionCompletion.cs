namespace Agnosia.Android.Activities;

internal sealed class PackageInstallSessionCompletion(int sessionId)
{
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<bool> Completion => _completion.Task;

    public void ReportFinished(int finishedSessionId, bool success)
    {
        if (finishedSessionId == sessionId) _completion.TrySetResult(success);
    }
}
