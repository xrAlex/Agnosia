using Agnosia.Android.Api.Storage;

namespace Agnosia.Android.Storage;

internal static class LockdownSettingsStore
{
    private const char PackageSeparator = '\n';

    public static bool IsEnabled()
    {
        return LocalStorageManager.Instance.GetBoolean(StorageKeys.LockdownEnabled);
    }

    public static void SetEnabled(bool enabled)
    {
        LocalStorageManager.Instance.SetBoolean(StorageKeys.LockdownEnabled, enabled);
    }

    public static string[] LoadBlockedPackages()
    {
        var savedValue = LocalStorageManager.Instance.GetString(StorageKeys.LockdownBlockedPackages);
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
            LocalStorageManager.Instance.Remove(StorageKeys.LockdownBlockedPackages);
            return;
        }

        LocalStorageManager.Instance.SetString(
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
