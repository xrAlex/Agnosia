using System.Text.Json;
using Agnosia.Android.Api.Platform;
using Agnosia.Android.Api.Serialization;
using Agnosia.Android.Api.Storage;
using Agnosia.Models;
using Android.Content;

namespace Agnosia.Android.Logging;

public static class AndroidAppLogArchive
{
    private const int MaxEntries = 100;
    private static readonly Lock Sync = new();
    private static readonly TimeSpan FlushDelay = TimeSpan.FromSeconds(1);
    private static readonly List<AppLogEntry> PendingEntries = [];

    private static Context? _flushContext;
    private static bool _flushScheduled;

    public static void Append(Context context, AppLogLevel level, string tag, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        AgnosiaRuntime.Initialize(context);
        var appContext = GetApplicationContext(context);
        lock (Sync)
        {
            if (!ServiceRegistry.GetRequiredService<LocalStorageManager>().GetBoolean(StorageKeys.LoggingEnabled, true)) return;

            PendingEntries.Add(CreateEntry(appContext, level, tag, message));

            Trim(PendingEntries);
            EnsureFlushScheduledLocked(appContext);
        }
    }

    public static IReadOnlyList<AppLogEntry> Load(Context context)
    {
        AgnosiaRuntime.Initialize(context);
        lock (Sync)
        {
            FlushPendingLocked(GetApplicationContext(context));
            return LoadCore();
        }
    }

    public static void Clear(Context context)
    {
        AgnosiaRuntime.Initialize(context);
        lock (Sync)
        {
            PendingEntries.Clear();
            ServiceRegistry.GetRequiredService<LocalStorageManager>().Remove(StorageKeys.LogEntries);
        }
    }

    private static void EnsureFlushScheduledLocked(Context context)
    {
        _flushContext = context;
        if (_flushScheduled) return;

        _flushScheduled = true;
        _ = FlushAfterDelayAsync();
    }

    private static async Task FlushAfterDelayAsync()
    {
        try
        {
            await Task.Delay(FlushDelay).ConfigureAwait(false);
            lock (Sync)
            {
                FlushPendingLocked(_flushContext);
                _flushScheduled = false;
            }
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn(nameof(AndroidAppLogArchive), $"LogAppend flush failed: {exception.Message}");
            lock (Sync)
            {
                _flushScheduled = false;
            }
        }
    }

    private static AppLogEntry CreateEntry(
        Context context,
        AppLogLevel level,
        string tag,
        string message)
    {
        return new AppLogEntry(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.Now,
            ResolveProfile(context),
            level,
            tag,
            message);
    }

    private static void FlushPendingLocked(Context? context)
    {
        if (context is not null) AgnosiaRuntime.Initialize(context);

        if (PendingEntries.Count == 0) return;

        if (!ServiceRegistry.GetRequiredService<LocalStorageManager>().GetBoolean(StorageKeys.LoggingEnabled, true))
        {
            PendingEntries.Clear();
            return;
        }

        var entries = LoadCore();
        entries.AddRange(PendingEntries);
        PendingEntries.Clear();
        Trim(entries);
        SaveCore(entries);
    }

    private static List<AppLogEntry> LoadCore()
    {
        var raw = ServiceRegistry.GetRequiredService<LocalStorageManager>().GetString(StorageKeys.LogEntries);
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        try
        {
            var entries = JsonSerializer.Deserialize(raw, AndroidApiJsonContext.Default.ListAppLogEntry) ?? [];
            Trim(entries);
            return entries;
        }
        catch (JsonException)
        {
            ServiceRegistry.GetRequiredService<LocalStorageManager>().Remove(StorageKeys.LogEntries);
            return [];
        }
    }

    private static void SaveCore(List<AppLogEntry> entries)
    {
        ServiceRegistry.GetRequiredService<LocalStorageManager>().SetString(
            StorageKeys.LogEntries,
            JsonSerializer.Serialize(entries, AndroidApiJsonContext.Default.ListAppLogEntry));
    }

    private static void Trim(List<AppLogEntry> entries)
    {
        while (entries.Count > MaxEntries) entries.RemoveAt(0);
    }

    private static Context GetApplicationContext(Context context)
    {
        return context.ApplicationContext ?? context;
    }

    private static ProfileKind ResolveProfile(Context context)
    {
        return AgnosiaUtilities.IsProfileOwner(context) ? ProfileKind.Work : ProfileKind.Personal;
    }
}
