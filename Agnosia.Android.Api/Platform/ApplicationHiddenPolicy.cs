namespace Agnosia.Android.Api.Platform;

public static class ApplicationHiddenPolicy
{
    public static bool Apply(bool hidden, Func<bool> read, Func<bool, bool> write, bool repairUnchangedPolicy)
    {
        write(hidden);
        if (read() == hidden) return true;
        if (!repairUnchangedPolicy) return false;

        // Android 14+ policy engine may remember a policy that no longer matches
        // PackageManager (for example after reinstall). Repeating it is a no-op.
        // Reset to the observed state first; never expose a confirmed hidden app.
        write(!hidden);
        write(hidden);
        return read() == hidden;
    }
}
