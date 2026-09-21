using System.Text.Json;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Gateways;

// Package names are hints for the by-name inventory fallback, not evidence that
// a package is still installed. The work profile verifies each candidate.
internal static class AndroidKnownWorkPackages
{
    private const string LogTag = "AgnosiaProfileCommand";
    private const string StorageKeyPrefix = "known_work_packages:";
    private static readonly object Sync = new();

    public static string[] Read(long profileSerial)
    {
        if (profileSerial < 0) return [];
        lock (Sync)
        {
            return ReadCore(GetStorage(), profileSerial);
        }
    }

    public static void Remember(string? packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName)) return;
        RememberAll([packageName]);
    }

    public static void RememberAll(IEnumerable<string> packageNames)
    {
        lock (Sync)
        {
            var storage = GetStorage();
            var serial = storage.GetLong(StorageKeys.ManagedProfileUserSerial, -1);
            if (serial < 0) return;
            var names = ReadCore(storage, serial).ToHashSet(StringComparer.Ordinal);
            var changed = false;
            foreach (var name in packageNames)
                if (!string.IsNullOrWhiteSpace(name)) changed |= names.Add(name);
            if (changed) WriteCore(storage, serial, names);
        }
    }

    public static void Forget(string? packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName)) return;
        lock (Sync)
        {
            var storage = GetStorage();
            var serial = storage.GetLong(StorageKeys.ManagedProfileUserSerial, -1);
            if (serial < 0) return;
            var names = ReadCore(storage, serial).ToHashSet(StringComparer.Ordinal);
            if (names.Remove(packageName)) WriteCore(storage, serial, names);
        }
    }

    private static LocalStorageManager GetStorage() =>
        ServiceRegistry.GetRequiredService<LocalStorageManager>();

    private static string[] ReadCore(LocalStorageManager storage, long serial)
    {
        var raw = storage.GetString(StorageKeyPrefix + serial);
        if (string.IsNullOrWhiteSpace(raw)) return [];
        try
        {
            return JsonSerializer.Deserialize<string[]>(raw)?
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .ToArray() ?? [];
        }
        catch (JsonException exception)
        {
            Log.Warn(LogTag,
                $"Known work package names are unreadable. profileSerial={serial}, error={exception.Message}.");
            return [];
        }
    }

    private static void WriteCore(LocalStorageManager storage, long serial, IEnumerable<string> names)
    {
        storage.SetString(StorageKeyPrefix + serial,
            JsonSerializer.Serialize(names.OrderBy(static name => name, StringComparer.Ordinal)));
    }
}
