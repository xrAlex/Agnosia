using Agnosia.Models;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Storage;

public static class AndroidSettingsStore
{
    private const string LogTag = "AgnosiaSettings";

    public static AppSettingsSnapshot LoadSnapshot(LocalStorageManager storage)
    {
        return new AppSettingsSnapshot(
            storage.GetBoolean(StorageKeys.ShowAllApps),
            storage.GetBoolean(StorageKeys.DisableVpnBeforeWorkLaunch),
            storage.GetBoolean(StorageKeys.CrossProfileFileShuttleEnabled),
            storage.GetBoolean(StorageKeys.LoggingEnabled, true),
            LoadAppTheme(storage),
            storage.GetBoolean(StorageKeys.EnableVpnAfterWorkFreeze),
            LoadVpnAfterWorkFreezeClient(storage),
            storage.GetString(StorageKeys.TunguskaAutomationToken) ?? string.Empty,
            LoadCommandTransportPreference(storage));
    }

    public static async Task<OperationResult> SaveAsync(
        Activity activity,
        AppSettingsSnapshot settings,
        CancellationToken cancellationToken = default)
    {
        AgnosiaRuntime.Initialize(activity);

        var storage = ServiceRegistry.GetRequiredService<LocalStorageManager>();
        var loggingChanged = storage.GetBoolean(StorageKeys.LoggingEnabled, true) != settings.LoggingEnabled;
        var fileShuttleChanged = storage.GetBoolean(StorageKeys.CrossProfileFileShuttleEnabled) !=
                                 settings.CrossProfileFileShuttleEnabled;
        var vpnAfterFreezeClientChanged = LoadVpnAfterWorkFreezeClient(storage) != settings.VpnAfterWorkFreezeClient;
        var tunguskaToken = AndroidSettingsContract.NormalizeTunguskaAutomationToken(settings.TunguskaAutomationToken);

        var manager = ServiceRegistry.GetRequiredService<SettingsManager>();
        await Task.Run(() => manager.PersistSnapshot(() => storage.SetValues(
            new Dictionary<string, bool>
            {
                [StorageKeys.ShowAllApps] = settings.ShowAllApps,
                [StorageKeys.DisableVpnBeforeWorkLaunch] = settings.DisableVpnBeforeWorkLaunch,
                [StorageKeys.CrossProfileFileShuttleEnabled] = settings.CrossProfileFileShuttleEnabled,
                [StorageKeys.EnableVpnAfterWorkFreeze] = settings.EnableVpnAfterWorkFreeze,
                [StorageKeys.LoggingEnabled] = settings.LoggingEnabled
            },
            new Dictionary<string, string>
            {
                [StorageKeys.VpnAfterWorkFreezeClient] = settings.VpnAfterWorkFreezeClient.ToString(),
                [StorageKeys.TunguskaAutomationToken] = tunguskaToken,
                [StorageKeys.AppTheme] = settings.Theme.ToString(),
                [StorageKeys.CommandTransportPreference] = settings.CommandTransport.ToString(),
                [PendingBooleanSettingsSync.Key(StorageKeys.LoggingEnabled)] = settings.LoggingEnabled ? "true" : "false",
                [PendingBooleanSettingsSync.Key(StorageKeys.DisableVpnBeforeWorkLaunch)] = settings.DisableVpnBeforeWorkLaunch ? "true" : "false",
                [PendingBooleanSettingsSync.Key(StorageKeys.CrossProfileFileShuttleEnabled)] = settings.CrossProfileFileShuttleEnabled ? "true" : "false"
            })), cancellationToken).ConfigureAwait(false);

        if (loggingChanged && !settings.LoggingEnabled) AndroidAppLogArchive.Clear(activity);
        if (fileShuttleChanged) AgnosiaUtilities.ApplyCrossProfileFileShuttleComponentState(activity);

        if (vpnAfterFreezeClientChanged)
            Log.Debug(LogTag, $"VPN after work freeze client changed: client={settings.VpnAfterWorkFreezeClient}.");

        return await manager.RetryPendingAsync(cancellationToken).ConfigureAwait(false);
    }

    public static AppThemeKind LoadAppTheme(LocalStorageManager storage)
    {
        return AndroidSettingsContract.ParseAppTheme(storage.GetString(StorageKeys.AppTheme));
    }

    public static VpnAutomationClientKind LoadVpnAfterWorkFreezeClient(LocalStorageManager storage)
    {
        return AndroidSettingsContract.ParseVpnAfterWorkFreezeClient(
            storage.GetString(StorageKeys.VpnAfterWorkFreezeClient));
    }

    public static CommandTransportPreference LoadCommandTransportPreference(LocalStorageManager storage)
    {
        return AndroidSettingsContract.ParseCommandTransportPreference(
            storage.GetString(StorageKeys.CommandTransportPreference));
    }
}
