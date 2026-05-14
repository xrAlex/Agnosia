using System.Text.Json;
using Agnosia.Android.Api.Platform;
using Agnosia.Android.Api.Storage;
using Agnosia.Models;
using Android.Content;

namespace Agnosia.Android.Api.Logging;

public static class AndroidAppLogArchive
{
    private const int MaxEntries = 200;
    private const string PerfLogTag = "AgnosiaPerf";

    private static readonly Lock Sync = new();
    private static readonly TimeSpan FlushDelay = TimeSpan.FromSeconds(1);
    private static readonly List<AppLogEntry> PendingEntries = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static Context? FlushContext;
    private static bool FlushScheduled;

    public static void Append(Context context, AppLogLevel level, string tag, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        AgnosiaRuntime.Initialize(context);
        var appContext = context.ApplicationContext ?? context;
        lock (Sync)
        {
            if (!LocalStorageManager.Instance.GetBoolean(StorageKeys.LoggingEnabled, true)) return;

            PendingEntries.Add(new AppLogEntry(
                Guid.NewGuid().ToString("N"),
                DateTimeOffset.Now,
                ResolveProfile(appContext),
                level,
                tag,
                message));

            Trim(PendingEntries);
            EnsureFlushScheduledLocked(appContext);
        }
    }

    public static IReadOnlyList<AppLogEntry> Load(Context context)
    {
        AgnosiaRuntime.Initialize(context);
        lock (Sync)
        {
            FlushPendingLocked(context.ApplicationContext ?? context);
            return LoadCore();
        }
    }

    public static void Clear(Context context)
    {
        AgnosiaRuntime.Initialize(context);
        lock (Sync)
        {
            PendingEntries.Clear();
            LocalStorageManager.Instance.Remove(StorageKeys.LogEntries);
        }
    }

    private static void EnsureFlushScheduledLocked(Context context)
    {
        FlushContext = context;
        if (FlushScheduled) return;

        FlushScheduled = true;
        _ = FlushAfterDelayAsync();
    }

    private static async Task FlushAfterDelayAsync()
    {
        try
        {
            await Task.Delay(FlushDelay).ConfigureAwait(false);
            lock (Sync)
            {
                FlushPendingLocked(FlushContext);
                FlushScheduled = false;
            }
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn(PerfLogTag, $"LogAppend flush failed: {exception.Message}");
            lock (Sync)
            {
                FlushScheduled = false;
            }
        }
    }

    private static void FlushPendingLocked(Context? context)
    {
        if (context is not null) AgnosiaRuntime.Initialize(context);

        if (PendingEntries.Count == 0) return;

        if (!LocalStorageManager.Instance.GetBoolean(StorageKeys.LoggingEnabled, true))
        {
            PendingEntries.Clear();
            return;
        }

        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var entries = LoadCore();
        entries.AddRange(PendingEntries);
        PendingEntries.Clear();
        Trim(entries);
        SaveCore(entries);
        var elapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        global::Android.Util.Log.Debug(PerfLogTag, $"LogAppend flush elapsedMs={elapsedMs:0.0}; count={entries.Count}");
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

    private static void SaveCore(List<AppLogEntry> entries)
    {
        LocalStorageManager.Instance.SetString(StorageKeys.LogEntries, JsonSerializer.Serialize(entries, JsonOptions));
    }

    private static void Trim(List<AppLogEntry> entries)
    {
        while (entries.Count > MaxEntries) entries.RemoveAt(0);
    }

    private static ProfileKind ResolveProfile(Context context)
    {
        return AgnosiaUtilities.IsProfileOwner(context) ? ProfileKind.Work : ProfileKind.Personal;
    }
}