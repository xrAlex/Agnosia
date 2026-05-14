using Agnosia.Models;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Agnosia.Infrastructure;

public static class AppThemeManager
{
    private static AppThemeKind? _appliedTheme;

    public static void Apply(AppThemeKind theme)
    {
        if (_appliedTheme == theme) return;

        if (Application.Current is not { } application) return;

        _appliedTheme = theme;
        var palette = ResolvePalette(theme);
        var shouldUpdateThemeVariant = application.RequestedThemeVariant != palette.ThemeVariant;

        SetColor(application, "SystemAccentColor", palette.Accent);
        SetColor(application, "SystemAccentColorDark1", palette.AccentPressed);
        SetColor(application, "SystemAccentColorDark2", palette.AccentMuted);
        SetColor(application, "SystemAccentColorDark3", palette.Border);
        SetColor(application, "SystemAccentColorLight1", palette.AccentHover);
        SetColor(application, "SystemAccentColorLight2", palette.AccentSoft);
        SetColor(application, "SystemAccentColorLight3", palette.AccentSoft);

        SetBrush(application, "AppBackgroundBrush", palette.Background);
        SetBrush(application, "AppChromeBrush", palette.Chrome);
        SetBrush(application, "AppSurfaceBrush", palette.Surface);
        SetBrush(application, "AppSurfaceAltBrush", palette.SurfaceAlt);
        SetBrush(application, "AppBorderBrush", palette.Border);
        SetBrush(application, "AppBorderSoftBrush", palette.BorderSoft);
        SetBrush(application, "AppAccentBrush", palette.Accent);
        SetBrush(application, "AppAccentPressedBrush", palette.AccentPressed);
        SetBrush(application, "AppAccentMutedBrush", palette.AccentMuted);
        SetBrush(application, "AppButtonSecondaryBrush", palette.ButtonSecondary);
        SetBrush(application, "AppButtonSecondaryHoverBrush", palette.ButtonSecondaryHover);
        SetBrush(application, "AppInputBrush", palette.Input);
        SetBrush(application, "AppTextPrimaryBrush", palette.TextPrimary);
        SetBrush(application, "AppTextSecondaryBrush", palette.TextSecondary);
        SetBrush(application, "AppTextMutedBrush", palette.TextMuted);
        SetBrush(application, "AppOnAccentBrush", palette.OnAccent);
        SetBrush(application, "AppOverlayScrimBrush", palette.OverlayScrim);
        SetBrush(application, "AppPlexusLineBrush", palette.PlexusLine);
        SetBrush(application, "AppPlexusNodeBrush", palette.PlexusNode);
        SetBrush(application, "AppPlexusGlowBrush", palette.PlexusGlow);

        if (shouldUpdateThemeVariant) application.RequestedThemeVariant = palette.ThemeVariant;
    }

    private static AppThemePalette ResolvePalette(AppThemeKind theme)
    {
        return theme switch
        {
            AppThemeKind.Dark => new AppThemePalette(
                ThemeVariant.Dark,
                Color.Parse("#101114"),
                Color.Parse("#17191D"),
                Color.Parse("#1D2025"),
                Color.Parse("#262A31"),
                Color.Parse("#3B4656"),
                Color.Parse("#2C333D"),
                Color.Parse("#4DA3FF"),
                Color.Parse("#6CB4FF"),
                Color.Parse("#2F7FD1"),
                Color.Parse("#183B5F"),
                Color.Parse("#9BCBFF"),
                Color.Parse("#20242A"),
                Color.Parse("#2B3038"),
                Color.Parse("#15181D"),
                Color.Parse("#F4F7FA"),
                Color.Parse("#C8D0DA"),
                Color.Parse("#91A0AF"),
                Color.Parse("#07111D"),
                Color.Parse("#AA05070A"),
                Color.Parse("#665FA8FF"),
                Color.Parse("#CC7DB8FF"),
                Color.Parse("#245FA8FF")),
            AppThemeKind.Light => new AppThemePalette(
                ThemeVariant.Light,
                Color.Parse("#F6F7F9"),
                Color.Parse("#FFFFFF"),
                Color.Parse("#FFFFFF"),
                Color.Parse("#EEF2F6"),
                Color.Parse("#C8D3DF"),
                Color.Parse("#DEE5EC"),
                Color.Parse("#176BCA"),
                Color.Parse("#2E7FDB"),
                Color.Parse("#0F559F"),
                Color.Parse("#DCEBFA"),
                Color.Parse("#8ABBF0"),
                Color.Parse("#F3F6FA"),
                Color.Parse("#E8EEF5"),
                Color.Parse("#FFFFFF"),
                Color.Parse("#18202A"),
                Color.Parse("#4D5C6C"),
                Color.Parse("#718194"),
                Color.Parse("#FFFFFF"),
                Color.Parse("#88F6F7F9"),
                Color.Parse("#554D8DDA"),
                Color.Parse("#AA176BCA"),
                Color.Parse("#204D8DDA")),
            _ => new AppThemePalette(
                ThemeVariant.Dark,
                Color.Parse("#08090B"),
                Color.Parse("#101216"),
                Color.Parse("#16191E"),
                Color.Parse("#20242B"),
                Color.Parse("#37404A"),
                Color.Parse("#242A32"),
                Color.Parse("#FF253A"),
                Color.Parse("#FF4355"),
                Color.Parse("#C9001D"),
                Color.Parse("#361821"),
                Color.Parse("#FF6F7B"),
                Color.Parse("#1B1F25"),
                Color.Parse("#252B34"),
                Color.Parse("#11141A"),
                Color.Parse("#F7F8FA"),
                Color.Parse("#C9D0D8"),
                Color.Parse("#98A2B0"),
                Color.Parse("#08090B"),
                Color.Parse("#AA05070A"),
                Color.Parse("#7AFF253A"),
                Color.Parse("#E6FF5663"),
                Color.Parse("#30FF253A"))
        };
    }

    private static void SetBrush(Application application, string key, Color color)
    {
        if (application.Resources.TryGetResource(key, application.ActualThemeVariant, out var resource)
            && resource is SolidColorBrush brush)
        {
            if (brush.Color == color) return;

            brush.Color = color;
            return;
        }

        application.Resources[key] = new SolidColorBrush(color);
    }

    private static void SetColor(Application application, string key, Color color)
    {
        if (application.Resources.TryGetResource(key, application.ActualThemeVariant, out var resource)
            && resource is Color existingColor
            && existingColor == color)
            return;

        application.Resources[key] = color;
    }

    private sealed record AppThemePalette(
        ThemeVariant ThemeVariant,
        Color Background,
        Color Chrome,
        Color Surface,
        Color SurfaceAlt,
        Color Border,
        Color BorderSoft,
        Color Accent,
        Color AccentHover,
        Color AccentPressed,
        Color AccentMuted,
        Color AccentSoft,
        Color ButtonSecondary,
        Color ButtonSecondaryHover,
        Color Input,
        Color TextPrimary,
        Color TextSecondary,
        Color TextMuted,
        Color OnAccent,
        Color OverlayScrim,
        Color PlexusLine,
        Color PlexusNode,
        Color PlexusGlow);
}