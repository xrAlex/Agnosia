using Agnosia.Models;

namespace Agnosia.Android.Modules;

internal sealed partial class AndroidModuleCoordinator
{
    private static AgnosiaModuleRequirement[] GetActivationRequirements(
        IReadOnlyList<PermissionSnapshot> permissions)
    {
        return
        [
            GetPermissionRequirement(permissions, PermissionKind.WorkProfile),
            GetPermissionRequirement(permissions, PermissionKind.PersonalAllFiles),
            GetPermissionRequirement(permissions, PermissionKind.WorkAllFiles)
        ];
    }

    private static AgnosiaModuleRequirement[] GetLockdownActivationRequirements(
        IReadOnlyList<PermissionSnapshot> permissions)
    {
        return
        [
            GetPermissionRequirement(permissions, PermissionKind.WorkProfile)
        ];
    }

    private static AgnosiaModuleRequirement[] GetVpnGuardActivationRequirements(
        IReadOnlyList<PermissionSnapshot> permissions)
    {
        return
        [
            GetPermissionRequirement(permissions, PermissionKind.WorkProfile),
            GetPermissionRequirement(permissions, PermissionKind.VpnControl),
            GetPermissionRequirement(permissions, PermissionKind.Overlay)
        ];
    }

    private static AgnosiaModuleRequirement GetPermissionRequirement(
        IReadOnlyList<PermissionSnapshot> permissions,
        PermissionKind kind)
    {
        return AgnosiaModuleRequirementFactory.FromPermission(GetPermission(permissions, kind));
    }

    private static PermissionSnapshot GetPermission(
        IReadOnlyList<PermissionSnapshot> permissions,
        PermissionKind kind)
    {
        return permissions.FirstOrDefault(permission => permission.Kind == kind)
               ?? PermissionCatalog.CreateSnapshot(kind, false, false);
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
