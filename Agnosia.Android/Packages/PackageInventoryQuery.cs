namespace Agnosia.Android.Packages;

internal static class PackageInventoryQuery
{
    public static IReadOnlyList<T> Read<T>(
        Func<IReadOnlyList<T>> queryApplications,
        CancellationToken cancellationToken = default,
        Func<IReadOnlyList<T>>? queryKnownPackages = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var apps = queryApplications();
        cancellationToken.ThrowIfCancellationRequested();
        if (apps.Count > 0) return apps;

        if (queryKnownPackages is not null)
        {
            apps = queryKnownPackages();
            cancellationToken.ThrowIfCancellationRequested();
        }

        // These are raw entries, before filtering system apps or Agnosia itself.
        // Even a profile without user-installed apps contains the running app.
        return apps.Count > 0 ? apps : throw new PackageInventoryUnavailableException();
    }

    public static IReadOnlyList<T> ReadKnownPackages<T>(
        IReadOnlyList<string> packageNames,
        Func<string, T?> lookup,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var apps = new List<T>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in packageNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(name) || !seen.Add(name)) continue;
            if (lookup(name) is { } app) apps.Add(app);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return apps;
    }
}

internal sealed class PackageInventoryUnavailableException() : InvalidOperationException(
    "Android не предоставил список приложений профиля.");
