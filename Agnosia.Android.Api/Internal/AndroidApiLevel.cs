using System.Runtime.Versioning;

namespace Agnosia.Android.Api.Internal;

[SupportedOSPlatform("android30.0")]
internal static class AndroidApiLevel
{
    [SupportedOSPlatformGuard("android31.0")]
    public static bool IsAtLeastS()
    {
        return OperatingSystem.IsAndroidVersionAtLeast(31);
    }

    [SupportedOSPlatformGuard("android34.0")]
    public static bool IsAtLeastUpsideDownCake()
    {
        return OperatingSystem.IsAndroidVersionAtLeast(34);
    }

    [SupportedOSPlatformGuard("android35.0")]
    public static bool IsAtLeastVanillaIceCream()
    {
        return OperatingSystem.IsAndroidVersionAtLeast(35);
    }
}