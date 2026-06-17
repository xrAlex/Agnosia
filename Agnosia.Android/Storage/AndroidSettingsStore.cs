using Agnosia.Android.Api.Logging;
using Agnosia.Android.Api.Platform;
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
            storage.GetString(StorageKeys.TunguskaAutomationToken) ?? string.Empty);
    }

    public static async Task<OperationResult> SaveAsync(
        Activity activity,
        AppSettingsSnapshot settings,
        CancellationToken cancellationToken = default)
    {
        AgnosiaRuntime.Initialize(activity);

        var storage = ServiceRegistry.GetRequiredService<LocalStorageManager>();
        var loggingChanged = storage.GetBoolean(StorageKeys.LoggingEnabled, true) != settings.LoggingEnabled;
        var disableVpnBeforeLaunchChanged = storage.GetBoolean(StorageKeys.DisableVpnBeforeWorkLaunch) !=
                                            settings.DisableVpnBeforeWorkLaunch;
        var fileShuttleChanged = storage.GetBoolean(StorageKeys.CrossProfileFileShuttleEnabled) !=
                                 settings.CrossProfileFileShuttleEnabled;
        var vpnAfterFreezeClientChanged = LoadVpnAfterWorkFreezeClient(storage) != settings.VpnAfterWorkFreezeClient;
        var tunguskaToken = AndroidSettingsContract.NormalizeTunguskaAutomationToken(settings.TunguskaAutomationToken);

        storage.SetBoolean(StorageKeys.ShowAllApps, settings.ShowAllApps);
        storage.SetBoolean(StorageKeys.DisableVpnBeforeWorkLaunch, settings.DisableVpnBeforeWorkLaunch);
        storage.SetBoolean(StorageKeys.CrossProfileFileShuttleEnabled, settings.CrossProfileFileShuttleEnabled);
        storage.SetBoolean(StorageKeys.EnableVpnAfterWorkFreeze, settings.EnableVpnAfterWorkFreeze);
        storage.SetString(StorageKeys.VpnAfterWorkFreezeClient, settings.VpnAfterWorkFreezeClient.ToString());
        storage.SetString(StorageKeys.TunguskaAutomationToken, tunguskaToken);
        storage.SetBoolean(StorageKeys.LoggingEnabled, settings.LoggingEnabled);
        storage.SetString(StorageKeys.AppTheme, settings.Theme.ToString());

        if (loggingChanged && !settings.LoggingEnabled) AndroidAppLogArchive.Clear(activity);
        if (fileShuttleChanged) AgnosiaUtilities.ApplyCrossProfileFileShuttleComponentState(activity);

        if (loggingChanged)
            await TrySyncBooleanSettingToWorkProfileAsync(activity, StorageKeys.LoggingEnabled, settings.LoggingEnabled,
                cancellationToken).ConfigureAwait(false);

        if (disableVpnBeforeLaunchChanged || settings.DisableVpnBeforeWorkLaunch)
            await TrySyncBooleanSettingToWorkProfileAsync(activity, StorageKeys.DisableVpnBeforeWorkLaunch,
                settings.DisableVpnBeforeWorkLaunch, cancellationToken).ConfigureAwait(false);

        if (fileShuttleChanged || settings.CrossProfileFileShuttleEnabled)
            await TrySyncBooleanSettingToWorkProfileAsync(activity, StorageKeys.CrossProfileFileShuttleEnabled,
                settings.CrossProfileFileShuttleEnabled, cancellationToken).ConfigureAwait(false);

        if (vpnAfterFreezeClientChanged)
            Log.Debug(LogTag, $"VPN after work freeze client changed: client={settings.VpnAfterWorkFreezeClient}.");

        return OperationResult.Success("Настройки сохранены.");
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

    private static async Task TrySyncBooleanSettingToWorkProfileAsync(
        Activity activity,
        string name,
        bool value,
        CancellationToken cancellationToken)
    {
        if (!AgnosiaUtilities.HasWorkProfileTarget(activity)) return;

        try
        {
            var result = await ServiceRegistry.GetRequiredService<SettingsManager>().SyncBooleanSettingAsync(name, value, cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded)
                Log.Warn(LogTag, $"Failed to synchronize {name} to the work profile: {result.Message}");
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to synchronize {name} to the work profile: {exception.Message}");
        }
    }
}
