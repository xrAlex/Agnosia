using Agnosia.Models;

namespace Agnosia.Android.Storage;

internal sealed class PendingBooleanSettingsSync(
    IReadOnlyList<string> names,
    Func<string, string?> read,
    Action<string, string> write,
    Action<string> remove)
{
    private readonly Lock _sync = new();
    private readonly SemaphoreSlim _drain = new(1, 1);

    public static string Key(string name) => "pending.setting." + name;

    public void PersistSnapshot(Action persist)
    {
        lock (_sync) persist();
    }

    public void Queue(string name, bool value)
    {
        if (!names.Contains(name)) throw new ArgumentException("Setting is not synchronized.", nameof(name));
        lock (_sync) write(Key(name), value ? "true" : "false");
    }

    public async Task<OperationResult> FlushAsync(
        Func<string, bool, CancellationToken, Task<OperationResult>> send,
        CancellationToken cancellationToken = default)
    {
        await _drain.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var failures = new List<string>();
            foreach (var name in names)
            {
                string? pending;
                lock (_sync) pending = read(Key(name));
                if (pending is null) continue;
                cancellationToken.ThrowIfCancellationRequested();
                OperationResult result;
                try
                {
                    result = await send(name, bool.Parse(pending), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception) { result = OperationResult.Failure(exception.Message); }
                lock (_sync)
                {
                    if (!result.Succeeded || read(Key(name)) != pending)
                    {
                        failures.Add(name);
                        continue;
                    }

                    try { remove(Key(name)); }
                    catch (Exception) { failures.Add(name); }
                }
            }
            return failures.Count == 0
                ? OperationResult.Success("Настройки сохранены и применены.")
                : OperationResult.Failure("Настройки сохранены на устройстве, но рабочий профиль не подтвердил применение. Синхронизация будет повторена.");
        }
        finally { _drain.Release(); }
    }
}
