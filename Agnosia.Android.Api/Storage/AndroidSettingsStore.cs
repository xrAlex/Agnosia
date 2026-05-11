using Agnosia.Models;
using Log = Agnosia.Android.Api.AgnosiaLog;

namespace Agnosia.Android.Api;

public static class AndroidSettingsStore
{
    private const string LogTag = "AgnosiaSettings";

    public static AppSettingsSnapshot LoadSnapshot(LocalStorageManager storage) =>
        new(
            storage.GetBoolean(StorageKeys.ShowAllApps),
            SettingsManager.Instance.GetBlockContactsSearchingEnabled(),
            storage.GetBoolean(StorageKeys.DisableVpnBeforeWorkLaunch),
            storage.GetBoolean(StorageKeys.LoggingEnabled, true),
            LoadAppTheme(storage),
            storage.GetBoolean(StorageKeys.EnableVpnAfterWorkFreeze),
            LoadVpnAfterWorkFreezeClient(storage),
            storage.GetString(StorageKeys.TunguskaAutomationToken) ?? string.Empty);

    public static async Task<OperationResult> SaveAsync(
        Activity activity,
        AppSettingsSnapshot settings,
        CancellationToken cancellationToken = default)
    {
        AgnosiaRuntime.Initialize(activity);

        var storage = LocalStorageManager.Instance;
        var loggingChanged = storage.GetBoolean(StorageKeys.LoggingEnabled, true) != settings.LoggingEnabled;
        var blockContactsChanged = SettingsManager.Instance.GetBlockContactsSearchingEnabled() != settings.BlockContactsSearching;
        var disableVpnBeforeLaunchChanged = storage.GetBoolean(StorageKeys.DisableVpnBeforeWorkLaunch) != settings.DisableVpnBeforeWorkLaunch;
        var vpnAfterFreezeClientChanged = LoadVpnAfterWorkFreezeClient(storage) != settings.VpnAfterWorkFreezeClient;
        var tunguskaToken = settings.TunguskaAutomationToken.Trim();

        storage.SetBoolean(StorageKeys.ShowAllApps, settings.ShowAllApps);
        storage.SetBoolean(StorageKeys.DisableVpnBeforeWorkLaunch, settings.DisableVpnBeforeWorkLaunch);
        storage.SetBoolean(StorageKeys.EnableVpnAfterWorkFreeze, settings.EnableVpnAfterWorkFreeze);
        storage.SetString(StorageKeys.VpnAfterWorkFreezeClient, settings.VpnAfterWorkFreezeClient.ToString());
        storage.SetString(StorageKeys.TunguskaAutomationToken, tunguskaToken);
        storage.SetBoolean(StorageKeys.LoggingEnabled, settings.LoggingEnabled);
        storage.SetString(StorageKeys.AppTheme, settings.Theme.ToString());
        SettingsManager.Instance.SetBlockContactsSearchingEnabled(settings.BlockContactsSearching, syncToWorkProfile: false);

        if (loggingChanged && !settings.LoggingEnabled)
        {
            AndroidAppLogArchive.Clear(activity);
        }

        if (loggingChanged)
        {
            await TrySyncBooleanSettingToWorkProfileAsync(activity, StorageKeys.LoggingEnabled, settings.LoggingEnabled, cancellationToken);
        }

        if (blockContactsChanged)
        {
            await TrySyncBooleanSettingToWorkProfileAsync(activity, StorageKeys.BlockContactsSearching, settings.BlockContactsSearching, cancellationToken);
        }

        if (disableVpnBeforeLaunchChanged || settings.DisableVpnBeforeWorkLaunch)
        {
            await TrySyncBooleanSettingToWorkProfileAsync(activity, StorageKeys.DisableVpnBeforeWorkLaunch, settings.DisableVpnBeforeWorkLaunch, cancellationToken);
        }

        if (vpnAfterFreezeClientChanged)
        {
            Log.Info(LogTag, $"VPN after work freeze client changed: client={settings.VpnAfterWorkFreezeClient}.");
        }

        return OperationResult.Success("Настройки сохранены.");
    }

    public static AppThemeKind LoadAppTheme(LocalStorageManager storage) =>
        Enum.TryParse<AppThemeKind>(storage.GetString(StorageKeys.AppTheme), ignoreCase: true, out var theme)
            ? theme
            : AppThemeKind.Agnosia;

    public static VpnAutomationClientKind LoadVpnAfterWorkFreezeClient(LocalStorageManager storage) =>
        Enum.TryParse<VpnAutomationClientKind>(
            storage.GetString(StorageKeys.VpnAfterWorkFreezeClient),
            ignoreCase: true,
            out var client)
            ? client
            : VpnAutomationClientKind.FlClash;

    private static async Task TrySyncBooleanSettingToWorkProfileAsync(
        Activity activity,
        string name,
        bool value,
        CancellationToken cancellationToken)
    {
        if (!AgnosiaUtilities.HasWorkProfileTarget(activity))
        {
            return;
        }

        try
        {
            var result = await SettingsManager.Instance.SyncBooleanSettingAsync(name, value, cancellationToken);
            if (!result.Succeeded)
            {
                Log.Warn(LogTag, $"Failed to synchronize {name} to the work profile: {result.Message}");
            }
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to synchronize {name} to the work profile: {exception.Message}");
        }
    }
}
