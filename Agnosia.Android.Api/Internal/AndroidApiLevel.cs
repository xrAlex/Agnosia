using System.Runtime.Versioning;

namespace Agnosia.Android.Api.Internal;

[SupportedOSPlatform("android30.0")]
internal static class AndroidApiLevel
{
    [SupportedOSPlatformGuard("android31.0")]
    public static bool IsAtLeastS() => OperatingSystem.IsAndroidVersionAtLeast(31);

    [SupportedOSPlatformGuard("android34.0")]
    public static bool IsAtLeastUpsideDownCake() => OperatingSystem.IsAndroidVersionAtLeast(34);

    [SupportedOSPlatformGuard("android35.0")]
    public static bool IsAtLeastVanillaIceCream() => OperatingSystem.IsAndroidVersionAtLeast(35);
}
