using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace OsuMusicPlayer.App.Themes;

/// <summary>Pushes a theme into the running UI; the view model only knows this interface.</summary>
public interface IThemeApplier
{
    void Apply(PlayerTheme theme);
}

/// <summary>
/// Writes the theme into the application's resource dictionary. The XAML refers to the
/// brushes with DynamicResource so every open window recolours immediately, and the Fluent
/// accent keys are overridden so sliders, toggles and selection use the same accent.
/// </summary>
public sealed class ApplicationThemeApplier : IThemeApplier
{
    public void Apply(PlayerTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (Application.Current is not { } application)
        {
            return;
        }

        application.RequestedThemeVariant = theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        Apply(application.Resources, theme);
    }

    public static void Apply(IResourceDictionary resources, PlayerTheme theme)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(theme);

        setBrush(resources, "ThemeBackgroundBrush", theme.Background);
        setBrush(resources, "ThemeSurfaceBrush", theme.Surface);
        setBrush(resources, "ThemeSurfaceAltBrush", theme.SurfaceAlt);
        setBrush(resources, "ThemeBorderBrush", theme.Border);
        setBrush(resources, "ThemeTextBrush", theme.Text);
        setBrush(resources, "ThemeTextMutedBrush", theme.TextMuted);
        setBrush(resources, "ThemeTextFaintBrush", theme.TextFaint);
        setBrush(resources, "ThemeAccentBrush", theme.Accent);
        setBrush(resources, "ThemeAccentTextBrush", theme.AccentText);
        setBrush(resources, "ThemeAccentSoftBrush", Color.FromArgb(0x33, theme.Accent.R, theme.Accent.G, theme.Accent.B));
        setBrush(resources, "ThemeSelectionBrush", Color.FromArgb(theme.IsDark ? (byte)0x40 : (byte)0x30, theme.Accent.R, theme.Accent.G, theme.Accent.B));

        // Fluent's accent palette (buttons, sliders, checkboxes, list selection).
        resources["SystemAccentColor"] = theme.Accent;
        resources["SystemAccentColorLight1"] = PlayerThemes.Shade(theme.Accent, 0.15);
        resources["SystemAccentColorLight2"] = PlayerThemes.Shade(theme.Accent, 0.30);
        resources["SystemAccentColorLight3"] = PlayerThemes.Shade(theme.Accent, 0.45);
        resources["SystemAccentColorDark1"] = PlayerThemes.Shade(theme.Accent, -0.15);
        resources["SystemAccentColorDark2"] = PlayerThemes.Shade(theme.Accent, -0.30);
        resources["SystemAccentColorDark3"] = PlayerThemes.Shade(theme.Accent, -0.45);
    }

    private static void setBrush(IResourceDictionary resources, string key, Color color)
    {
        if (resources.TryGetResource(key, null, out var existing) && existing is SolidColorBrush brush && !brush.IsFrozen())
        {
            brush.Color = color;
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }
}

file static class BrushExtensions
{
    // Brushes created here are never sealed, so the fast path (mutating the colour) always applies.
    public static bool IsFrozen(this SolidColorBrush _) => false;
}
