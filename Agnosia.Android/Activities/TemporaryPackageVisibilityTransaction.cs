namespace Agnosia.Android.Activities;

internal sealed class TemporaryPackageVisibilityTransaction(bool wasHidden)
{
    public bool RollbackRequired { get; private set; }

    public void MarkPackageUnhidden()
    {
        RollbackRequired = wasHidden;
    }

    public void Commit()
    {
        RollbackRequired = false;
    }
}

internal static class PackageRemovalVisibility
{
    public static bool ShouldRollback(bool restoreHiddenState, bool uninstallSucceeded)
    {
        return restoreHiddenState && !uninstallSucceeded;
    }
}
