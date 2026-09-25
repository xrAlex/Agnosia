using Agnosia.Android.Api.Vpn;
using Agnosia.Models;
using Xunit;

namespace Agnosia.Unit.AndroidApi.Vpn;

public sealed class VpnTaskerClientCatalogTests
{
    [Theory]
    [InlineData(VpnAutomationClientKind.V2RayNg, "com.v2ray.ang", "com.v2ray.ang.fdroid", "com.v2ray.ang.receiver.TaskerReceiver")]
    [InlineData(VpnAutomationClientKind.OlcNg, "xyz.zarazaex.olc", "xyz.zarazaex.olc.fdroid", "xyz.zarazaex.olc.receiver.TaskerReceiver")]
    [InlineData(VpnAutomationClientKind.V2RayTun, "com.v2raytun.android", null, "com.v2raytun.android.receiver.TaskerReceiver")]
    public void Clients_try_the_standard_package_before_fdroid(
        VpnAutomationClientKind kind,
        string standardPackage,
        string? fdroidPackage,
        string receiverClassName)
    {
        var client = VpnTaskerClientCatalog.Clients.Single(client => client.Kind == kind);

        Assert.Equal(standardPackage, client.PackageNames[0]);
        Assert.Equal(fdroidPackage, client.PackageNames.Skip(1).SingleOrDefault());
        Assert.Equal(receiverClassName, client.ReceiverClassName);
        Assert.Equal("com.twofortyfouram.locale.intent.action.FIRE_SETTING", VpnTaskerClientCatalog.FireSettingAction);
    }

    [Theory]
    [InlineData(true, true, true, true, "com.v2ray.ang")]
    [InlineData(false, true, false, true, "com.v2ray.ang.fdroid")]
    [InlineData(true, true, false, true, "com.v2ray.ang.fdroid")]
    [InlineData(false, false, false, false, "com.v2ray.ang")]
    public void Package_selection_prefers_the_first_available_handler(
        bool standardInstalled,
        bool fdroidInstalled,
        bool standardHandlerAvailable,
        bool fdroidHandlerAvailable,
        string expectedPackage)
    {
        var packages = VpnTaskerClientCatalog.Clients.Single(client => client.Kind == VpnAutomationClientKind.V2RayNg).PackageNames;
        var selected = VpnClientPackageSelector.Select(
            packages,
            package => package == packages[0] ? standardInstalled : fdroidInstalled,
            package => package == packages[0] ? standardHandlerAvailable : fdroidHandlerAvailable);

        Assert.Equal(expectedPackage, selected);
    }
}
