using Agnosia.Models;
using Android.Content;

namespace Agnosia.Android.Storage;

public sealed class SettingsManager
{
    private readonly Context _context;
    private readonly PendingBooleanSettingsSync _pending;
    internal static readonly string[] SynchronizedNames =
        [StorageKeys.LoggingEnabled, StorageKeys.DisableVpnBeforeWorkLaunch, StorageKeys.CrossProfileFileShuttleEnabled,
            StorageKeys.RiskEngineEnabled];

    public SettingsManager(Context context)
    {
        _context = context;
        var storage = ServiceRegistry.GetRequiredService<LocalStorageManager>();
        _pending = new PendingBooleanSettingsSync(
            SynchronizedNames,
            storage.GetString,
            storage.SetStringDurably,
            storage.RemoveDurably);
    }

    internal void PersistSnapshot(Action persist) => _pending.PersistSnapshot(persist);

    public Task<OperationResult> RetryPendingAsync(CancellationToken cancellationToken = default) =>
        _pending.FlushAsync((name, value, token) =>
        {
            if (!AgnosiaUtilities.HasWorkProfileTarget(_context))
                return Task.FromResult(OperationResult.Failure("Рабочий профиль недоступен."));
            return AndroidProfileCommandGateway.SynchronizeBooleanToWorkProfileAsync(_context, name, value, token);
        }, cancellationToken);
}
