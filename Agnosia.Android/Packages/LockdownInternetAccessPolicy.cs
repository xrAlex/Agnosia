namespace Agnosia.Android.Packages;

internal static class LockdownInternetAccessPolicy
{
    public static HashSet<string> ResolveActiveBlockedPackages(bool lockdownEnabled, IEnumerable<string> savedPackages)
    {
        return lockdownEnabled
            ? savedPackages.ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }
}
