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
        if (_appliedTheme == theme)
        {
            return;
        }

        if (Application.Current is not { } application)
        {
            return;
        }

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

        if (shouldUpdateThemeVariant)
        {
            application.RequestedThemeVariant = palette.ThemeVariant;
        }
    }

    private static AppThemePalette ResolvePalette(AppThemeKind theme) =>
        theme switch
        {
            AppThemeKind.Dark => new AppThemePalette(
                ThemeVariant.Dark,
                Background: Color.Parse("#101114"),
                Chrome: Color.Parse("#17191D"),
                Surface: Color.Parse("#1D2025"),
                SurfaceAlt: Color.Parse("#262A31"),
                Border: Color.Parse("#3B4656"),
                BorderSoft: Color.Parse("#2C333D"),
                Accent: Color.Parse("#4DA3FF"),
                AccentHover: Color.Parse("#6CB4FF"),
                AccentPressed: Color.Parse("#2F7FD1"),
                AccentMuted: Color.Parse("#183B5F"),
                AccentSoft: Color.Parse("#9BCBFF"),
                ButtonSecondary: Color.Parse("#20242A"),
                ButtonSecondaryHover: Color.Parse("#2B3038"),
                Input: Color.Parse("#15181D"),
                TextPrimary: Color.Parse("#F4F7FA"),
                TextSecondary: Color.Parse("#C8D0DA"),
                TextMuted: Color.Parse("#91A0AF"),
                OnAccent: Color.Parse("#07111D"),
                OverlayScrim: Color.Parse("#AA05070A"),
                PlexusLine: Color.Parse("#665FA8FF"),
                PlexusNode: Color.Parse("#CC7DB8FF"),
                PlexusGlow: Color.Parse("#245FA8FF")),
            AppThemeKind.Light => new AppThemePalette(
                ThemeVariant.Light,
                Background: Color.Parse("#F6F7F9"),
                Chrome: Color.Parse("#FFFFFF"),
                Surface: Color.Parse("#FFFFFF"),
                SurfaceAlt: Color.Parse("#EEF2F6"),
                Border: Color.Parse("#C8D3DF"),
                BorderSoft: Color.Parse("#DEE5EC"),
                Accent: Color.Parse("#176BCA"),
                AccentHover: Color.Parse("#2E7FDB"),
                AccentPressed: Color.Parse("#0F559F"),
                AccentMuted: Color.Parse("#DCEBFA"),
                AccentSoft: Color.Parse("#8ABBF0"),
                ButtonSecondary: Color.Parse("#F3F6FA"),
                ButtonSecondaryHover: Color.Parse("#E8EEF5"),
                Input: Color.Parse("#FFFFFF"),
                TextPrimary: Color.Parse("#18202A"),
                TextSecondary: Color.Parse("#4D5C6C"),
                TextMuted: Color.Parse("#718194"),
                OnAccent: Color.Parse("#FFFFFF"),
                OverlayScrim: Color.Parse("#88F6F7F9"),
                PlexusLine: Color.Parse("#554D8DDA"),
                PlexusNode: Color.Parse("#AA176BCA"),
                PlexusGlow: Color.Parse("#204D8DDA")),
            _ => new AppThemePalette(
                ThemeVariant.Dark,
                Background: Color.Parse("#08090B"),
                Chrome: Color.Parse("#101216"),
                Surface: Color.Parse("#16191E"),
                SurfaceAlt: Color.Parse("#20242B"),
                Border: Color.Parse("#37404A"),
                BorderSoft: Color.Parse("#242A32"),
                Accent: Color.Parse("#FF253A"),
                AccentHover: Color.Parse("#FF4355"),
                AccentPressed: Color.Parse("#C9001D"),
                AccentMuted: Color.Parse("#361821"),
                AccentSoft: Color.Parse("#FF6F7B"),
                ButtonSecondary: Color.Parse("#1B1F25"),
                ButtonSecondaryHover: Color.Parse("#252B34"),
                Input: Color.Parse("#11141A"),
                TextPrimary: Color.Parse("#F7F8FA"),
                TextSecondary: Color.Parse("#C9D0D8"),
                TextMuted: Color.Parse("#98A2B0"),
                OnAccent: Color.Parse("#08090B"),
                OverlayScrim: Color.Parse("#AA05070A"),
                PlexusLine: Color.Parse("#7AFF253A"),
                PlexusNode: Color.Parse("#E6FF5663"),
                PlexusGlow: Color.Parse("#30FF253A"))
        };

    private static void SetBrush(Application application, string key, Color color)
    {
        if (application.Resources.TryGetResource(key, application.ActualThemeVariant, out var resource)
            && resource is SolidColorBrush brush)
        {
            if (brush.Color == color)
            {
                return;
            }

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
        {
            return;
        }

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
