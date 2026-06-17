namespace Agnosia.Android.Storage;

internal static class LockdownSettingsStore
{
    private const char PackageSeparator = '\n';

    public static bool IsEnabled()
    {
        return ServiceRegistry.GetRequiredService<LocalStorageManager>().GetBoolean(StorageKeys.LockdownEnabled);
    }

    public static void SetEnabled(bool enabled)
    {
        ServiceRegistry.GetRequiredService<LocalStorageManager>().SetBoolean(StorageKeys.LockdownEnabled, enabled);
    }

    public static string[] LoadBlockedPackages()
    {
        var savedValue = ServiceRegistry.GetRequiredService<LocalStorageManager>().GetString(StorageKeys.LockdownBlockedPackages);
        if (string.IsNullOrWhiteSpace(savedValue)) return [];

        return savedValue
            .Split(PackageSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static packageName => !string.IsNullOrWhiteSpace(packageName))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static void SaveBlockedPackages(IEnumerable<string> packageNames)
    {
        var normalized = packageNames
            .Where(static packageName => !string.IsNullOrWhiteSpace(packageName))
            .Select(static packageName => packageName.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (normalized.Length == 0)
        {
            ServiceRegistry.GetRequiredService<LocalStorageManager>().Remove(StorageKeys.LockdownBlockedPackages);
            return;
        }

        ServiceRegistry.GetRequiredService<LocalStorageManager>().SetString(
            StorageKeys.LockdownBlockedPackages,
            string.Join(PackageSeparator, normalized));
    }

    public static string[] SetPackageBlocked(string packageName, bool blocked)
    {
        var packages = LoadBlockedPackages().ToHashSet(StringComparer.Ordinal);
        if (blocked)
            packages.Add(packageName);
        else
            packages.Remove(packageName);

        var updatedPackages = packages.Order(StringComparer.Ordinal).ToArray();
        SaveBlockedPackages(updatedPackages);
        return updatedPackages;
    }
}
