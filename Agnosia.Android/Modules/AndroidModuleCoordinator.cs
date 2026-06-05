using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Permissions;
using Agnosia.Android.Api.Platform;
using Agnosia.Android.Api.Storage;
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
        var permissions = await permissionCoordinator.LoadPermissionsAsync(cancellationToken);

        return
        [
            CreateFileShuttleSnapshot(activity, permissions),
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
            AgnosiaModuleKind.FileShuttle => await SetFileShuttleEnabledAsync(enabled, cancellationToken),
            AgnosiaModuleKind.VpnGuard => await SetVpnGuardEnabledAsync(enabled, cancellationToken),
            AgnosiaModuleKind.RiskEngine => await SetRiskEngineEnabledAsync(enabled, cancellationToken),
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
            var permissions = await permissionCoordinator.LoadPermissionsAsync(cancellationToken);
            var missingRequirements = GetActivationRequirements(permissions)
                .Where(requirement => !requirement.IsSatisfied)
                .ToArray();

            if (missingRequirements.Length > 0)
                return OperationResult.Failure("Сначала выполните требования File Shuttle.");
        }

        var storage = LocalStorageManager.Instance;
        storage.SetBoolean(StorageKeys.CrossProfileFileShuttleEnabled, enabled);
        AgnosiaUtilities.ApplyCrossProfileFileShuttleComponentState(activity);

        var syncResult = await TrySyncFileShuttleSettingAsync(activity, enabled, cancellationToken);
        if (!syncResult.Succeeded) return syncResult;

        if (enabled && !IsFileShuttleProviderEnabled(activity))
            return OperationResult.Failure("Android не смог включить компонент File Shuttle.");

        return OperationResult.Success(enabled ? "File Shuttle включён." : "File Shuttle выключен.");
    }

    private static AgnosiaModuleSnapshot CreateFileShuttleSnapshot(
        Context context,
        IReadOnlyList<PermissionSnapshot> permissions)
    {
        var storage = LocalStorageManager.Instance;
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

        return new AgnosiaModuleSnapshot(
            AgnosiaModuleKind.FileShuttle,
            "File Shuttle",
            "Передача файлов между личным и рабочим профилем через Files / DocumentsUI.",
            """
            File Shuttle открывает хранилище второго профиля в системном Files / DocumentsUI.
            Он не делает чужой профиль обычной папкой: сторонние приложения получают только выбранные content:// URI через Storage Access Framework Android.
            """,
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
            var permissions = await permissionCoordinator.LoadPermissionsAsync(cancellationToken);
            var missingRequirements = GetVpnGuardActivationRequirements(permissions)
                .Where(requirement => !requirement.IsSatisfied)
                .ToArray();

            if (missingRequirements.Length > 0)
                return OperationResult.Failure("Сначала выполните требования VPN Guard.");
        }

        var storage = LocalStorageManager.Instance;
        storage.SetBoolean(StorageKeys.DisableVpnBeforeWorkLaunch, enabled);
        storage.SetBoolean(StorageKeys.EnableVpnAfterWorkFreeze, enabled);
        if (!enabled) storage.SetBoolean(StorageKeys.HaveActiveVpnSession, false);

        var syncResult = await TrySyncBooleanSettingAsync(
            activity,
            StorageKeys.DisableVpnBeforeWorkLaunch,
            enabled,
            "VPN Guard",
            cancellationToken);
        if (!syncResult.Succeeded) return syncResult;

        return OperationResult.Success(enabled ? "VPN Guard включён." : "VPN Guard выключен.");
    }

    private static AgnosiaModuleSnapshot CreateVpnGuardSnapshot(
        IReadOnlyList<PermissionSnapshot> permissions)
    {
        var storage = LocalStorageManager.Instance;
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

        return new AgnosiaModuleSnapshot(
            AgnosiaModuleKind.VpnGuard,
            "VPN Guard",
            "Временное отключение VPN перед запуском рабочего приложения и возврат после заморозки.",
            """
            VPN Guard объединяет два действия в один сценарий: перед запуском скрытого рабочего приложения Agnosia временно отключает активный VPN в личном профиле, а после заморозки приложения запускает выбранный VPN-клиент обратно.
            Клиент для восстановления выбирается в настройках.
            """,
            isFullyEnabled,
            state,
            activationRequirements,
            GetVpnGuardStatusText(state),
            isFullyEnabled || !missingActivationRequirements);
    }

    private async Task<OperationResult> SetRiskEngineEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        var activity = commandRunner.CurrentActivity;
        AgnosiaRuntime.Initialize(activity);

        var storage = LocalStorageManager.Instance;
        storage.SetBoolean(StorageKeys.RiskEngineEnabled, enabled);

        var syncResult = await TrySyncBooleanSettingAsync(
            activity,
            StorageKeys.RiskEngineEnabled,
            enabled,
            "Risk Engine",
            cancellationToken);
        if (!syncResult.Succeeded) return syncResult;

        return OperationResult.Success(enabled ? "Risk Engine включён." : "Risk Engine выключен.");
    }

    private static AgnosiaModuleSnapshot CreateRiskEngineSnapshot()
    {
        var isEnabled = LocalStorageManager.Instance.GetBoolean(StorageKeys.RiskEngineEnabled, true);
        var state = isEnabled ? AgnosiaModuleState.Enabled : AgnosiaModuleState.Disabled;

        return new AgnosiaModuleSnapshot(
            AgnosiaModuleKind.RiskEngine,
            "Risk Engine",
            "Анализ риска приложений по разрешениям, специальным доступам и runtime-состояниям.",
            """
            Risk Engine оценивает приложения по комбинациям manifest-разрешений, runtime-доступов, специальных системных доступов и target SDK.
            Когда модуль выключен, каталог приложений не считает и не показывает риск по разрешениям.
            """,
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
                "Позволяет Agnosia кратко занять VPN-слот Android и отключить активный VPN перед запуском рабочего приложения.",
                vpnControl.IsGranted,
                PermissionKind.VpnControl,
                vpnControl.RequestLabel),
            new AgnosiaModuleRequirement(
                "Overlay window",
                "Позволяет VPN Guard показывать технический overlay-индикатор во время VPN-сценария.",
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
            var result = await SettingsManager.Instance.SyncBooleanSettingAsync(
                StorageKeys.CrossProfileFileShuttleEnabled,
                enabled,
                cancellationToken);
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
            var result = await SettingsManager.Instance.SyncBooleanSettingAsync(key, enabled, cancellationToken);
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
