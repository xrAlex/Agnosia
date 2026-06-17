using Agnosia.Models;

namespace Agnosia.Android.Modules;

internal sealed partial class AndroidModuleCoordinator
{
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

    private static AgnosiaModuleRequirement[] GetLockdownActivationRequirements(
        IReadOnlyList<PermissionSnapshot> permissions)
    {
        var workProfile = GetPermission(permissions, PermissionKind.WorkProfile);

        return
        [
            new AgnosiaModuleRequirement(
                "Рабочий профиль",
                "Нужен рабочий profile owner, чтобы Agnosia могла включить always-on VPN lockdown.",
                workProfile.IsGranted,
                PermissionKind.WorkProfile,
                workProfile.RequestLabel)
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

    private static AgnosiaModuleState ResolveVpnGuardState(
        bool isFullyEnabled,
        bool hasPartialSettings,
        bool workProfileAvailable,
        bool missingActivationRequirements)
    {
        if (!workProfileAvailable && (isFullyEnabled || hasPartialSettings)) return AgnosiaModuleState.Unavailable;
        if (isFullyEnabled && missingActivationRequirements) return AgnosiaModuleState.PartiallyEnabled;
        if (hasPartialSettings && !isFullyEnabled) return AgnosiaModuleState.PartiallyEnabled;
        return isFullyEnabled ? AgnosiaModuleState.Enabled : AgnosiaModuleState.Disabled;
    }

    private static AgnosiaModuleState ResolveLockdownState(
        bool isSettingEnabled,
        bool workProfileAvailable)
    {
        if (!isSettingEnabled) return AgnosiaModuleState.Disabled;
        return workProfileAvailable ? AgnosiaModuleState.Enabled : AgnosiaModuleState.Unavailable;
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

    private static string GetVpnGuardStatusText(AgnosiaModuleState state)
    {
        return state switch
        {
            AgnosiaModuleState.Enabled => "Включён",
            AgnosiaModuleState.PartiallyEnabled => "Частично включён",
            AgnosiaModuleState.Unavailable => "Недоступен",
            _ => "Выключен"
        };
    }

    private static string GetLockdownStatusText(AgnosiaModuleState state)
    {
        return state switch
        {
            AgnosiaModuleState.Enabled => "Включён",
            AgnosiaModuleState.Unavailable => "Недоступен",
            _ => "Выключен"
        };
    }

    private static string GetEnabledStatusText(AgnosiaModuleState state)
    {
        return state == AgnosiaModuleState.Enabled ? "Включён" : "Выключен";
    }
}
