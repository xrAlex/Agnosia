using Agnosia.Android.Api.Gateways;
using Agnosia.Android.Api.Logging;
using Agnosia.Models;
using Android.Content;

namespace Agnosia.Android.Api.Storage;

public sealed class SettingsManager
{
    private const string LogTag = "AgnosiaSettings";
    private static SettingsManager? _instance;

    private readonly Context _context;
    private readonly LocalStorageManager _storage;

    private SettingsManager(Context context)
    {
        _context = context;
        _storage = LocalStorageManager.Instance;
    }

    public static void Initialize(Context context)
    {
        _instance = new SettingsManager(context);
    }

    public static SettingsManager Instance =>
        _instance ?? throw new InvalidOperationException("SettingsManager has not been initialized yet.");

    public bool GetBlockContactsSearchingEnabled()
    {
        return _storage.GetBoolean(StorageKeys.BlockContactsSearching, true);
    }

    public void SetBlockContactsSearchingEnabled(bool enabled, bool syncToWorkProfile = true)
    {
        _storage.SetBoolean(StorageKeys.BlockContactsSearching, enabled);
        if (syncToWorkProfile) SyncBooleanSetting(StorageKeys.BlockContactsSearching, enabled);
    }

    private void SyncBooleanSetting(string name, bool value)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await SyncBooleanSettingAsync(name, value);
                if (!result.Succeeded)
                    AgnosiaLog.Warn(LogTag, $"Failed to synchronize {name} to the work profile: {result.Message}");
            }
            catch (Exception exception)
            {
                AgnosiaLog.Warn(LogTag, $"Failed to synchronize {name} to the work profile: {exception.Message}");
            }
        });
    }

    public Task<OperationResult> SyncBooleanSettingAsync(
        string name,
        bool value,
        CancellationToken cancellationToken = default)
    {
        return AndroidProfileCommandGateway.SynchronizeBooleanToWorkProfileAsync(_context, name, value,
            cancellationToken);
    }
}