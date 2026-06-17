using Agnosia.Models;
using Xunit;

namespace Agnosia.Unit.Models;

public sealed class AgnosiaModuleRequirementFactoryTests
{
    [Fact]
    public void From_permission_uses_catalog_text()
    {
        var permission = new PermissionSnapshot(
            PermissionKind.VpnControl,
            "Changed title",
            "Changed profile",
            "Changed description",
            false,
            true,
            "Changed granted",
            "Changed action");

        var requirement = AgnosiaModuleRequirementFactory.FromPermission(permission);

        Assert.Equal("Временное управление VPN", requirement.Title);
        Assert.Equal("Позволяет приложению управлять VPN-соединениями", requirement.Description);
        Assert.Equal(permission.IsGranted, requirement.IsSatisfied);
        Assert.Equal(permission.Kind, requirement.PermissionKind);
        Assert.Equal("Разрешить", requirement.ActionLabel);
    }
}
