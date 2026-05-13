using Agnosia.Models;
using Agnosia.Platform;

namespace Agnosia.Infrastructure;

public static class ServiceRegistry
{
    public static IPlatformBridge PlatformBridge { get; set; } = UnsupportedPlatformBridge.Instance;

    public static AppThemeKind InitialTheme { get; set; } = AppThemeKind.Agnosia;

    public static bool SuppressPrimaryUiStartup { get; set; }
}
