using System.Text.Json;
using Agnosia.Models;
using Android.Content;

namespace Agnosia.Android.Logging;

public static class AndroidAppLogArchive
{
    private const int MaxEntries = 100;
    private const int MaxMessageLength = 4096;
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
            try
            {
                FlushPendingLocked(GetApplicationContext(context));
                return LoadCore(GetApplicationContext(context));
            }
            catch (Exception exception) when (IsArchiveIoFailure(exception))
            {
                global::Android.Util.Log.Warn(
                    nameof(AndroidAppLogArchive),
                    $"Log archive read failed: {exception.Message}");
                return PendingEntries.ToArray();
            }
        }
    }

    public static void Clear(Context context)
    {
        AgnosiaRuntime.Initialize(context);
        lock (Sync)
        {
            PendingEntries.Clear();
            ServiceRegistry.GetRequiredService<LocalStorageManager>().Remove(StorageKeys.LogEntries);
            try
            {
                File.Delete(GetArchivePath(GetApplicationContext(context)));
            }
            catch (Exception exception) when (IsArchiveIoFailure(exception))
            {
                global::Android.Util.Log.Warn(
                    nameof(AndroidAppLogArchive),
                    $"Log archive clear failed: {exception.Message}");
            }
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
            message.Length <= MaxMessageLength ? message : message[..MaxMessageLength]);
    }

    private static void FlushPendingLocked(Context? context)
    {
        if (context is not null) AgnosiaRuntime.Initialize(context);

        if (PendingEntries.Count == 0 || context is null) return;

        if (!ServiceRegistry.GetRequiredService<LocalStorageManager>().GetBoolean(StorageKeys.LoggingEnabled, true))
        {
            PendingEntries.Clear();
            return;
        }

        var entries = LoadCore(context);
        entries.AddRange(PendingEntries);
        Trim(entries);
        SaveCore(context, entries);
        PendingEntries.Clear();
    }

    private static List<AppLogEntry> LoadCore(Context context)
    {
        var path = GetArchivePath(context);
        var storage = ServiceRegistry.GetRequiredService<LocalStorageManager>();
        var raw = File.Exists(path) ? File.ReadAllText(path) : storage.GetString(StorageKeys.LogEntries);
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        try
        {
            var entries = JsonSerializer.Deserialize(raw, AndroidApiJsonContext.Default.ListAppLogEntry) ?? [];
            Trim(entries);
            if (!File.Exists(path))
            {
                SaveCore(context, entries);
                storage.Remove(StorageKeys.LogEntries);
            }
            return entries;
        }
        catch (JsonException)
        {
            ServiceRegistry.GetRequiredService<LocalStorageManager>().Remove(StorageKeys.LogEntries);
            File.Delete(path);
            return [];
        }
    }

    private static void SaveCore(Context context, List<AppLogEntry> entries)
    {
        var path = GetArchivePath(context);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(entries, AndroidApiJsonContext.Default.ListAppLogEntry));
        File.Move(temporary, path, true);
    }

    private static string GetArchivePath(Context context) => Path.Combine(
        context.FilesDir?.AbsolutePath ?? throw new IOException("Android files directory is unavailable."),
        "agnosia-log.json");

    private static bool IsArchiveIoFailure(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException;
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
