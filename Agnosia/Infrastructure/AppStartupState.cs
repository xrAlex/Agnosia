using Agnosia.Models;

namespace Agnosia.Infrastructure;

public sealed class AppStartupState
{
    public AppThemeKind InitialTheme { get; set; } = AppThemeKind.Agnosia;

    public bool SuppressPrimaryUiStartup { get; set; }
}
