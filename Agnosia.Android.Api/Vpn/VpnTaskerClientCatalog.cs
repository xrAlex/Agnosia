using Agnosia.Models;

namespace Agnosia.Android.Api.Vpn;

public sealed record VpnTaskerClientDefinition(
    VpnAutomationClientKind Kind,
    string DisplayName,
    string[] PackageNames,
    string ReceiverClassName);

public static class VpnTaskerClientCatalog
{
    public const string FireSettingAction = "com.twofortyfouram.locale.intent.action.FIRE_SETTING";
    public const string BundleExtra = "com.twofortyfouram.locale.intent.extra.BUNDLE";
    public const string SwitchExtra = "tasker_extra_bundle_switch";
    public const string GuidExtra = "tasker_extra_bundle_guid";
    public const string DefaultGuid = "Default";

    public static IReadOnlyList<VpnTaskerClientDefinition> Clients { get; } = Array.AsReadOnly<VpnTaskerClientDefinition>(
    [
        new(VpnAutomationClientKind.V2RayNg, "v2rayNG", ["com.v2ray.ang", "com.v2ray.ang.fdroid"], "com.v2ray.ang.receiver.TaskerReceiver"),
        new(VpnAutomationClientKind.OlcNg, "olcng", ["xyz.zarazaex.olc", "xyz.zarazaex.olc.fdroid"], "xyz.zarazaex.olc.receiver.TaskerReceiver"),
        new(VpnAutomationClientKind.V2RayTun, "v2RayTun", ["com.v2raytun.android"], "com.v2raytun.android.receiver.TaskerReceiver")
    ]);
}

public static class VpnClientPackageSelector
{
    public static string Select(
        IReadOnlyList<string> packageNames,
        Func<string, bool> isInstalled,
        Func<string, bool> canStart)
    {
        ArgumentNullException.ThrowIfNull(packageNames);
        ArgumentNullException.ThrowIfNull(isInstalled);
        ArgumentNullException.ThrowIfNull(canStart);
        if (packageNames.Count == 0) throw new ArgumentException("At least one VPN package is required.", nameof(packageNames));

        string? firstInstalled = null;
        foreach (var packageName in packageNames)
        {
            if (!isInstalled(packageName)) continue;
            firstInstalled ??= packageName;
            if (canStart(packageName)) return packageName;
        }

        return firstInstalled ?? packageNames[0];
    }
}
