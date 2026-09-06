using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OsuMusicPlayer.App.Input;
using OsuMusicPlayer.App.Services;
using OsuMusicPlayer.App.Themes;

namespace OsuMusicPlayer.App.ViewModels;

/// <summary>Theme selection, accent override and the shortcut list shown in Settings.</summary>
public sealed partial class MainWindowViewModel
{
    private readonly IThemeApplier? themeApplier;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentTheme))]
    private string selectedThemeName = PlayerThemes.DefaultName;

    /// <summary>Hex colour typed by the user; blank keeps the preset's accent.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentTheme))]
    [NotifyPropertyChangedFor(nameof(AccentColorStatusText))]
    private string accentColorText = string.Empty;

    public IReadOnlyList<string> ThemeNames => PlayerThemes.Names;

    public PlayerTheme CurrentTheme => PlayerThemes.Resolve(SelectedThemeName, AccentColorText);

    public string AccentColorStatusText => string.IsNullOrWhiteSpace(AccentColorText)
        ? "Preset accent"
        : PlayerThemes.TryParseColor(AccentColorText, out _) ? "Custom accent" : "Enter a colour like #FF66AA";

    public IReadOnlyList<Shortcut> Shortcuts => ShortcutMap.All;

    [RelayCommand]
    private void ResetAccentColor() => AccentColorText = string.Empty;

    partial void OnSelectedThemeNameChanged(string value) => applyTheme();

    partial void OnAccentColorTextChanged(string value) => applyTheme();

    private void applyTheme()
    {
        try
        {
            themeApplier?.Apply(CurrentTheme);
        }
        catch (InvalidOperationException)
        {
            // Applying a theme before the UI exists is harmless; it is applied again on restore.
        }
    }

    private void applyRestoredAppearance(AppSettings settings)
    {
        SelectedThemeName = PlayerThemes.Names.FirstOrDefault(name => string.Equals(name, settings.Appearance.ThemeName, StringComparison.OrdinalIgnoreCase)) ?? PlayerThemes.DefaultName;
        AccentColorText = PlayerThemes.TryParseColor(settings.Appearance.AccentColor, out _) ? settings.Appearance.AccentColor : string.Empty;
        applyTheme();
    }
}
