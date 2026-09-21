namespace Agnosia.Android.Activities;

internal static class NewPackageInstallRecovery
{
    public static async Task<bool> WaitAsync(
        bool wasInstalled,
        Func<bool> isInstalled,
        Func<CancellationToken, Task> waitForNextCheck,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // An existing copy cannot prove that this install (or update) succeeded.
        if (wasInstalled) return false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (isInstalled()) return true;
            await waitForNextCheck(cancellationToken).ConfigureAwait(false);
        }
    }
}
