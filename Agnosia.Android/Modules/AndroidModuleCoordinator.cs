using Agnosia.Models;
using Android.Content;
using Android.Content.PM;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Modules;

internal sealed partial class AndroidModuleCoordinator(
    AndroidActivityCommandGateway commandRunner,
    AndroidPermissionCoordinator permissionCoordinator)
{
    private const string LogTag = "AgnosiaModules";

    public async Task<IReadOnlyList<AgnosiaModuleSnapshot>> LoadModulesAsync(
        CancellationToken cancellationToken = default)
    {
        var activity = commandRunner.CurrentActivity;
        AgnosiaRuntime.Initialize(activity);
        var permissions = await permissionCoordinator.LoadPermissionsAsync(cancellationToken).ConfigureAwait(false);
        return CreateModuleSnapshots(activity, permissions);
    }

    public Task<IReadOnlyList<AgnosiaModuleSnapshot>> LoadModulesAsync(
        IReadOnlyList<PermissionSnapshot> permissions,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<IReadOnlyList<AgnosiaModuleSnapshot>>(cancellationToken);

        var activity = commandRunner.CurrentActivity;
        AgnosiaRuntime.Initialize(activity);
        return Task.FromResult(CreateModuleSnapshots(activity, permissions));
    }

    private static IReadOnlyList<AgnosiaModuleSnapshot> CreateModuleSnapshots(
        Context activity,
        IReadOnlyList<PermissionSnapshot> permissions)
    {
        return
        [
            CreateFileShuttleSnapshot(activity, permissions),
            CreateLockdownSnapshot(permissions),
            CreateVpnGuardSnapshot(permissions),
            CreateRiskEngineSnapshot()
        ];
    }

    public async Task<OperationResult> SetModuleEnabledAsync(
        AgnosiaModuleKind module,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        return module switch
        {
            AgnosiaModuleKind.FileShuttle => await SetFileShuttleEnabledAsync(enabled, cancellationToken)
                .ConfigureAwait(false),
            AgnosiaModuleKind.Lockdown => await SetLockdownEnabledAsync(enabled, cancellationToken)
                .ConfigureAwait(false),
            AgnosiaModuleKind.VpnGuard => await SetVpnGuardEnabledAsync(enabled, cancellationToken)
                .ConfigureAwait(false),
            AgnosiaModuleKind.RiskEngine => await SetRiskEngineEnabledAsync(enabled, cancellationToken)
                .ConfigureAwait(false),
            _ => OperationResult.Failure("Неизвестный модуль.")
        };
    }

    private async Task<OperationResult> SetFileShuttleEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        var activity = commandRunner.CurrentActivity;
        AgnosiaRuntime.Initialize(activity);

        if (enabled)
        {
            var permissions = await permissionCoordinator.LoadPermissionsAsync(cancellationToken)
                .ConfigureAwait(false);
            var missingRequirements = GetActivationRequirements(permissions)
                .Where(requirement => !requirement.IsSatisfied)
                .ToArray();

            if (missingRequirements.Length > 0)
                return OperationResult.Failure("Сначала выполните требования File Shuttle.");
        }

        var storage = ServiceRegistry.GetRequiredService<LocalStorageManager>();
        storage.SetBoolean(StorageKeys.CrossProfileFileShuttleEnabled, enabled);
        AgnosiaUtilities.ApplyCrossProfileFileShuttleComponentState(activity);

        var syncResult = await TrySyncFileShuttleSettingAsync(activity, enabled, cancellationToken)
            .ConfigureAwait(false);
        if (!syncResult.Succeeded) return syncResult;

        if (enabled && !IsFileShuttleProviderEnabled(activity))
            return OperationResult.Failure("Android не смог включить компонент File Shuttle.");

        return OperationResult.Success(enabled ? "File Shuttle включён." : "File Shuttle выключен.");
    }

    private static AgnosiaModuleSnapshot CreateFileShuttleSnapshot(
        Context context,
        IReadOnlyList<PermissionSnapshot> permissions)
    {
        var storage = ServiceRegistry.GetRequiredService<LocalStorageManager>();
        var isSettingEnabled = storage.GetBoolean(StorageKeys.CrossProfileFileShuttleEnabled);
        var activationRequirements = GetActivationRequirements(permissions);
        var providerRequirement = new AgnosiaModuleRequirement(
            "Компонент DocumentsUI",
            "Включается автоматически после активации модуля и показывает File Shuttle в системном Files.",
            !isSettingEnabled || IsFileShuttleProviderEnabled(context));
        var requirements = activationRequirements.Concat([providerRequirement]).ToArray();
        var missingActivationRequirements = activationRequirements.Any(requirement => !requirement.IsSatisfied);
        var workProfileAvailable = activationRequirements
            .First(requirement => requirement.PermissionKind == PermissionKind.WorkProfile)
            .IsSatisfied;

        var state = ResolveFileShuttleState(
            isSettingEnabled,
            workProfileAvailable,
            missingActivationRequirements,
            providerRequirement.IsSatisfied);

        return AgnosiaModuleSnapshot.Create(
            AgnosiaModuleCatalog.FileShuttle,
            isSettingEnabled,
            state,
            requirements,
            GetFileShuttleStatusText(state),
            isSettingEnabled || !missingActivationRequirements);
    }

    private async Task<OperationResult> SetVpnGuardEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        var activity = commandRunner.CurrentActivity;
        AgnosiaRuntime.Initialize(activity);

        if (enabled)
        {
            var permissions = await permissionCoordinator.LoadPermissionsAsync(cancellationToken)
                .ConfigureAwait(false);
            var missingRequirements = GetVpnGuardActivationRequirements(permissions)
                .Where(requirement => !requirement.IsSatisfied)
                .ToArray();

            if (missingRequirements.Length > 0)
                return OperationResult.Failure("Сначала выполните требования VPN Guard.");
        }

        var storage = ServiceRegistry.GetRequiredService<LocalStorageManager>();
        storage.SetBoolean(StorageKeys.DisableVpnBeforeWorkLaunch, enabled);
        storage.SetBoolean(StorageKeys.EnableVpnAfterWorkFreeze, enabled);
        if (!enabled) storage.SetBoolean(StorageKeys.HaveActiveVpnSession, false);

        var syncResult = await TrySyncBooleanSettingAsync(
                activity,
                StorageKeys.DisableVpnBeforeWorkLaunch,
                enabled,
                "VPN Guard",
                cancellationToken)
            .ConfigureAwait(false);
        return !syncResult.Succeeded ? syncResult : OperationResult.Success(enabled ? "VPN Guard включён." : "VPN Guard выключен.");
    }

    private static AgnosiaModuleSnapshot CreateVpnGuardSnapshot(
        IReadOnlyList<PermissionSnapshot> permissions)
    {
        var storage = ServiceRegistry.GetRequiredService<LocalStorageManager>();
        var disableBeforeLaunchEnabled = storage.GetBoolean(StorageKeys.DisableVpnBeforeWorkLaunch);
        var enableAfterFreezeEnabled = storage.GetBoolean(StorageKeys.EnableVpnAfterWorkFreeze);
        var activationRequirements = GetVpnGuardActivationRequirements(permissions);
        var workProfileAvailable = activationRequirements
            .First(requirement => requirement.PermissionKind == PermissionKind.WorkProfile)
            .IsSatisfied;
        var missingActivationRequirements = activationRequirements.Any(requirement => !requirement.IsSatisfied);
        var isFullyEnabled = disableBeforeLaunchEnabled && enableAfterFreezeEnabled;
        var hasPartialSettings = disableBeforeLaunchEnabled || enableAfterFreezeEnabled;
        var state = ResolveVpnGuardState(
            isFullyEnabled,
            hasPartialSettings,
            workProfileAvailable,
            missingActivationRequirements);

        return AgnosiaModuleSnapshot.Create(
            AgnosiaModuleCatalog.VpnGuard,
            isFullyEnabled,
            state,
            activationRequirements,
            GetVpnGuardStatusText(state),
            isFullyEnabled || !missingActivationRequirements);
    }

    private async Task<OperationResult> SetLockdownEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        var activity = commandRunner.CurrentActivity;
        AgnosiaRuntime.Initialize(activity);

        if (enabled)
        {
            var permissions = await permissionCoordinator.LoadPermissionsAsync(cancellationToken)
                .ConfigureAwait(false);
            var missingRequirements = GetLockdownActivationRequirements(permissions)
                .Where(requirement => !requirement.IsSatisfied)
                .ToArray();

            if (missingRequirements.Length > 0)
                return OperationResult.Failure("Сначала выполните требования Lockdown.");
        }

        if (!AgnosiaUtilities.HasWorkProfileTarget(activity))
            return OperationResult.Failure("Рабочий профиль недоступен для настройки Lockdown.");

        var result = await AndroidProfileCommandGateway.SetLockdownEnabledAsync(
                commandRunner,
                enabled,
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded) return result;

        ServiceRegistry.GetRequiredService<LocalStorageManager>().SetBoolean(StorageKeys.LockdownEnabled, enabled);
        return OperationResult.Success(enabled ? "Lockdown включён." : "Lockdown выключен.");
    }

    private static AgnosiaModuleSnapshot CreateLockdownSnapshot(
        IReadOnlyList<PermissionSnapshot> permissions)
    {
        var storage = ServiceRegistry.GetRequiredService<LocalStorageManager>();
        var isSettingEnabled = storage.GetBoolean(StorageKeys.LockdownEnabled);
        var activationRequirements = GetLockdownActivationRequirements(permissions);
        var workProfileAvailable = activationRequirements
            .First(requirement => requirement.PermissionKind == PermissionKind.WorkProfile)
            .IsSatisfied;
        var state = ResolveLockdownState(isSettingEnabled, workProfileAvailable);

        return AgnosiaModuleSnapshot.Create(
            AgnosiaModuleCatalog.Lockdown,
            isSettingEnabled,
            state,
            activationRequirements,
            GetLockdownStatusText(state),
            isSettingEnabled || workProfileAvailable);
    }

    private async Task<OperationResult> SetRiskEngineEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        var activity = commandRunner.CurrentActivity;
        AgnosiaRuntime.Initialize(activity);

        var storage = ServiceRegistry.GetRequiredService<LocalStorageManager>();
        storage.SetBoolean(StorageKeys.RiskEngineEnabled, enabled);

        var syncResult = await TrySyncBooleanSettingAsync(
                activity,
                StorageKeys.RiskEngineEnabled,
                enabled,
                "Risk Engine",
                cancellationToken)
            .ConfigureAwait(false);
        return !syncResult.Succeeded ? syncResult : OperationResult.Success(enabled ? "Risk Engine включён." : "Risk Engine выключен.");
    }

    private static AgnosiaModuleSnapshot CreateRiskEngineSnapshot()
    {
        var isEnabled = ServiceRegistry.GetRequiredService<LocalStorageManager>().GetBoolean(StorageKeys.RiskEngineEnabled, true);
        var state = isEnabled ? AgnosiaModuleState.Enabled : AgnosiaModuleState.Disabled;

        return AgnosiaModuleSnapshot.Create(
            AgnosiaModuleCatalog.RiskEngine,
            isEnabled,
            state,
            [],
            GetEnabledStatusText(state),
            true);
    }

    private static AgnosiaModuleRequirement[] GetVpnGuardActivationRequirements(
        IReadOnlyList<PermissionSnapshot> permissions)
    {
        var workProfile = GetPermission(permissions, PermissionKind.WorkProfile);
        var vpnControl = GetPermission(permissions, PermissionKind.VpnControl);
        var overlay = GetPermission(permissions, PermissionKind.Overlay);

        return
        [
            new AgnosiaModuleRequirement(
                "Рабочий профиль",
                "Нужен рабочий профиль, чтобы Agnosia могла запускать и снова скрывать изолированные приложения.",
                workProfile.IsGranted,
                PermissionKind.WorkProfile,
                workProfile.RequestLabel),
            new AgnosiaModuleRequirement(
                "Управление VPN",
                "Позволяет Agnosia отключить активный VPN перед запуском рабочего приложения.",
                vpnControl.IsGranted,
                PermissionKind.VpnControl,
                vpnControl.RequestLabel),
            new AgnosiaModuleRequirement(
                "Overlay window",
                "Позволяет Agnosia запускать выбранный VPN клиент после заморозки приложения.",
                overlay.IsGranted,
                PermissionKind.Overlay,
                overlay.RequestLabel)
        ];
    }

    private static bool IsFileShuttleProviderEnabled(Context context)
    {
        if (context.PackageManager is not { } packageManager || string.IsNullOrWhiteSpace(context.PackageName))
            return false;

        var component = new ComponentName(
            context.PackageName,
            AndroidCommandContract.FileShuttleDocumentsProviderComponent);
        return packageManager.GetComponentEnabledSetting(component) == ComponentEnabledState.Enabled;
    }

    private static async Task<OperationResult> TrySyncFileShuttleSettingAsync(
        Context context,
        bool enabled,
        CancellationToken cancellationToken)
    {
        if (!AgnosiaUtilities.HasWorkProfileTarget(context))
            return enabled
                ? OperationResult.Failure("Рабочий профиль недоступен для синхронизации File Shuttle.")
                : OperationResult.Success("Рабочий профиль недоступен, локальный File Shuttle выключен.");

        try
        {
            var result = await ServiceRegistry.GetRequiredService<SettingsManager>().SyncBooleanSettingAsync(
                    StorageKeys.CrossProfileFileShuttleEnabled,
                    enabled,
                    cancellationToken)
                .ConfigureAwait(false);
            if (result.Succeeded) return OperationResult.Success(string.Empty);

            return OperationResult.Failure(
                string.IsNullOrWhiteSpace(result.Message)
                    ? "Не удалось синхронизировать File Shuttle с рабочим профилем."
                    : result.Message);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to sync File Shuttle setting: {exception.Message}");
            return OperationResult.Failure("Не удалось синхронизировать File Shuttle с рабочим профилем.");
        }
    }

    private static async Task<OperationResult> TrySyncBooleanSettingAsync(
        Context context,
        string key,
        bool enabled,
        string moduleName,
        CancellationToken cancellationToken)
    {
        if (!AgnosiaUtilities.HasWorkProfileTarget(context))
            return enabled
                ? OperationResult.Failure($"Рабочий профиль недоступен для синхронизации {moduleName}.")
                : OperationResult.Success($"Рабочий профиль недоступен, локальный {moduleName} выключен.");

        try
        {
            var result = await ServiceRegistry.GetRequiredService<SettingsManager>().SyncBooleanSettingAsync(key, enabled, cancellationToken)
                .ConfigureAwait(false);
            if (result.Succeeded) return OperationResult.Success(string.Empty);

            return OperationResult.Failure(
                string.IsNullOrWhiteSpace(result.Message)
                    ? $"Не удалось синхронизировать {moduleName} с рабочим профилем."
                    : result.Message);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, $"Failed to sync {moduleName} setting: {exception.Message}");
            return OperationResult.Failure($"Не удалось синхронизировать {moduleName} с рабочим профилем.");
        }
    }
}
