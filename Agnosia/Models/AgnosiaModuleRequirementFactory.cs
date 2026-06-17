namespace Agnosia.Models;

internal static class AgnosiaModuleRequirementFactory
{
    public static AgnosiaModuleRequirement FromPermission(PermissionSnapshot permission)
    {
        return PermissionCatalog.CreateRequirement(permission);
    }
}
