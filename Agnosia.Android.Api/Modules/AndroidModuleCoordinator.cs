using Agnosia.Android.Api.Commands;
using Agnosia.Android.Api.Gateways;
using Agnosia.Android.Api.Permissions;
using Agnosia.Android.Api.Platform;
using Agnosia.Android.Api.Storage;
using Agnosia.Models;
using Android.Content;
using Android.Content.PM;
using Log = Agnosia.Android.Api.Logging.AgnosiaLog;

namespace Agnosia.Android.Api.Modules;

internal sealed class AndroidModuleCoordinator(
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

        return [CreateFileShuttleSnapshot(activity, permissions)];
    }

    public async Task<OperationResult> SetModuleEnabledAsync(
        AgnosiaModuleKind module,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        return module switch
        {
            AgnosiaModuleKind.FileShuttle => await SetFileShuttleEnabledAsync(enabled, cancellationToken),
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

    private static AgnosiaModuleRequirement[] GetActivationRequirements(
        IReadOnlyList<PermissionSnapshot> permissions)
    {
        var workProfile = GetPermission(permissions, PermissionKind.WorkProfile);
        var personalAllFiles = GetPermission(permissions, PermissionKind.PersonalAllFiles);
        var workAllFiles = GetPermission(permissions, PermissionKind.WorkAllFiles);

        return
        [
            new AgnosiaModuleRequirement(
                "Рабочий профиль",
                "Нужен второй профиль, чтобы File Shuttle мог открыть файловый мост между personal и work.",
                workProfile.IsGranted,
                PermissionKind.WorkProfile,
                workProfile.RequestLabel),
            new AgnosiaModuleRequirement(
                "Доступ к файлам в личном профиле",
                "Позволяет Agnosia отдавать выбранные файлы личного профиля через DocumentsUI.",
                personalAllFiles.IsGranted,
                PermissionKind.PersonalAllFiles,
                personalAllFiles.RequestLabel),
            new AgnosiaModuleRequirement(
                "Доступ к файлам в рабочем профиле",
                "Позволяет Agnosia отдавать выбранные файлы рабочего профиля через DocumentsUI.",
                workAllFiles.IsGranted,
                PermissionKind.WorkAllFiles,
                workAllFiles.RequestLabel)
        ];
    }

    private static PermissionSnapshot GetPermission(
        IReadOnlyList<PermissionSnapshot> permissions,
        PermissionKind kind)
    {
        return permissions.FirstOrDefault(permission => permission.Kind == kind)
               ?? new PermissionSnapshot(
                   kind,
                   kind.ToString(),
                   string.Empty,
                   string.Empty,
                   false,
                   false,
                   "Получено",
                   "Открыть");
    }

    private static AgnosiaModuleState ResolveFileShuttleState(
        bool isSettingEnabled,
        bool workProfileAvailable,
        bool missingActivationRequirements,
        bool providerRequirementSatisfied)
    {
        if (!isSettingEnabled) return AgnosiaModuleState.Disabled;
        if (!workProfileAvailable) return AgnosiaModuleState.Unavailable;
        if (missingActivationRequirements || !providerRequirementSatisfied) return AgnosiaModuleState.PartiallyEnabled;

        return AgnosiaModuleState.Enabled;
    }

    private static string GetFileShuttleStatusText(AgnosiaModuleState state)
    {
        return state switch
        {
            AgnosiaModuleState.Enabled => "Включён",
            AgnosiaModuleState.PartiallyEnabled => "Требует разрешений",
            AgnosiaModuleState.Unavailable => "Недоступен",
            _ => "Выключен"
        };
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
}
