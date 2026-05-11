using System.Text.Json;
using Agnosia.Models;
using Android.Content;

namespace Agnosia.Android.Api;

public static class AndroidAppLogArchive
{
    private const int MaxEntries = 100;

    private static readonly Lock Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static void Append(Context context, AppLogLevel level, string tag, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        AgnosiaRuntime.Initialize(context);
        lock (Sync)
        {
            if (!LocalStorageManager.Instance.GetBoolean(StorageKeys.LoggingEnabled, true))
            {
                return;
            }

            var entries = LoadCore();
            entries.Add(new AppLogEntry(
                Guid.NewGuid().ToString("N"),
                DateTimeOffset.Now,
                ResolveProfile(context),
                level,
                tag,
                message));

            Trim(entries);
            SaveCore(entries);
        }
    }

    public static IReadOnlyList<AppLogEntry> Load(Context context)
    {
        AgnosiaRuntime.Initialize(context);
        lock (Sync)
        {
            return LoadCore();
        }
    }

    public static void Clear(Context context)
    {
        AgnosiaRuntime.Initialize(context);
        lock (Sync)
        {
            LocalStorageManager.Instance.Remove(StorageKeys.LogEntries);
        }
    }

    private static List<AppLogEntry> LoadCore()
    {
        var raw = LocalStorageManager.Instance.GetString(StorageKeys.LogEntries);
        if (string.IsNullOrWhiteSpace(raw))
            return [];
        
        try
        {
            var entries = JsonSerializer.Deserialize<List<AppLogEntry>>(raw, JsonOptions) ?? [];
            Trim(entries);
            return entries;
        }
        catch (JsonException)
        {
            LocalStorageManager.Instance.Remove(StorageKeys.LogEntries);
            return [];
        }
    }

    private static void SaveCore(List<AppLogEntry> entries) =>
        LocalStorageManager.Instance.SetString(StorageKeys.LogEntries, JsonSerializer.Serialize(entries, JsonOptions));

    private static void Trim(List<AppLogEntry> entries)
    {
        while (entries.Count > MaxEntries)
        {
            entries.RemoveAt(0);
        }
    }

    private static ProfileKind ResolveProfile(Context context) =>
        AgnosiaUtilities.IsProfileOwner(context) ? ProfileKind.Work : ProfileKind.Personal;
}
