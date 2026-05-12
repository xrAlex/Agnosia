using Agnosia.Models;

namespace Agnosia.Services;

public sealed class BoundedAppEventLogService : IAppEventLogService
{
    private const int DefaultCapacity = 200;

    private readonly Lock _sync = new();
    private readonly HashSet<string> _platformLogIds = new(StringComparer.Ordinal);
    private readonly Queue<string> _entries;

    public BoundedAppEventLogService(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Event log capacity must be positive.");
        }

        Capacity = capacity;
        _entries = new Queue<string>(capacity);
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _entries.Count;
            }
        }
    }

    public int Capacity { get; }

    public string Summary => $"Сообщений: {Count} / {Capacity}";

    public string Output
    {
        get
        {
            lock (_sync)
            {
                return _entries.Count == 0
                    ? "Журнал пока пуст."
                    : string.Join(Environment.NewLine, _entries);
            }
        }
    }

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_sync)
            {
                return _entries.Count == 0
                    ? ["Журнал пока пуст."]
                    : _entries.ToArray();
            }
        }
    }

    public bool ImportPlatformLogs(IEnumerable<AppLogEntry> logs)
    {
        ArgumentNullException.ThrowIfNull(logs);

        lock (_sync)
        {
            var hasChanges = false;
            foreach (var entry in logs
                         .OrderBy(log => log.Timestamp)
                         .ThenBy(log => log.Id, StringComparer.Ordinal))
            {
                if (!_platformLogIds.Add(entry.Id))
                {
                    continue;
                }

                _entries.Enqueue(FormatPlatformLogEntry(entry));
                hasChanges = true;
            }

            if (hasChanges)
            {
                Trim();
            }

            return hasChanges;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _platformLogIds.Clear();
            _entries.Clear();
        }
    }

    private void Trim()
    {
        while (_entries.Count > Capacity)
        {
            _entries.Dequeue();
        }
    }

    private static string FormatPlatformLogEntry(AppLogEntry entry)
    {
        var level = entry.Level switch
        {
            AppLogLevel.Debug => "DBG",
            AppLogLevel.Warning => "WRN",
            AppLogLevel.Error => "ERR",
            _ => "INF"
        };

        var profile = entry.Profile == ProfileKind.Work ? "WRK" : "PER";
        var source = string.IsNullOrWhiteSpace(entry.Tag) ? profile : $"{profile}/{entry.Tag}";
        return $"{entry.Timestamp:HH:mm:ss}  {level}  [{source}] {entry.Message}";
    }
}
