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
    private readonly IBackgroundAccentColorExtractor accentColorExtractor;
    private Avalonia.Media.Color? beatmapAccent;
    private TrackItemViewModel? accentTrack;
    private long accentRequestVersion;
    internal Task PendingAccentUpdate { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentTheme))]
    [NotifyPropertyChangedFor(nameof(AccentColorStatusText))]
    private bool useBeatmapAccentColor;

    partial void OnUseBeatmapAccentColorChanged(bool value) => refreshBeatmapAccent(force: true);

    private void refreshBeatmapAccent(bool force = false)
    {
        if (disposed) return;
        var track = UseBeatmapAccentColor ? CurrentTrack ?? SelectedTrack : null;
        if (!force && ReferenceEquals(accentTrack, track)) return;
        accentTrack = track;
        var version = ++accentRequestVersion;
        beatmapAccent = null;
        notifyBeatmapAccentChanged();
        PendingAccentUpdate = track is null ? Task.CompletedTask : loadBeatmapAccentAsync(track, version);
    }

    private async Task loadBeatmapAccentAsync(TrackItemViewModel track, long version)
    {
        var color = await accentColorExtractor.ExtractAsync(track.Model.BackgroundFilePath, lifetimeCancellation.Token).ConfigureAwait(false);
        if (lifetimeCancellation.IsCancellationRequested) return;
        await dispatcher.InvokeAsync(() =>
        {
            if (disposed || version != accentRequestVersion) return;
            beatmapAccent = color;
            notifyBeatmapAccentChanged();
        }).ConfigureAwait(false);
    }

    private void notifyBeatmapAccentChanged()
    {
        OnPropertyChanged(nameof(CurrentTheme));
        OnPropertyChanged(nameof(AccentColorStatusText));
        applyTheme();
    }
    private readonly CustomizationStore customizationStore;
    private CustomizationSnapshot customization = new([], new(), []);

    public string CustomizationFolderPath => customizationStore.RootPath;

    [ObservableProperty]
    private string customizationStatusText = string.Empty;

    [RelayCommand]
    private void OpenCustomizationFolder()
    {
        if (!linkOpener.OpenFolder(CustomizationFolderPath))
            CustomizationStatusText = $"Custom folder: {CustomizationFolderPath}";
    }

    [RelayCommand]
    private async Task ReloadCustomizationAsync()
    {
        try
        {
            var snapshot = await Task.Run(() => customizationStore.LoadAsync(lifetimeCancellation.Token), lifetimeCancellation.Token).ConfigureAwait(false);
            await dispatcher.InvokeAsync(() =>
            {
                updateCustomization(snapshot);
                applyTheme();
                ActivePreviewSkin = OsuMusicPlayer.Core.Skins.PreviewSkin.Default;
                refreshPreviewSkins();
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested) { }
    }

    private void updateCustomization(CustomizationSnapshot snapshot)
    {
        customization = snapshot;
        // A ComboBox can clear its selection when ItemsSource changes.
        var selected = SelectedThemeName;
        OnPropertyChanged(nameof(ThemeNames));
        SelectedThemeName = ThemeNames.FirstOrDefault(name => name.Equals(selected, StringComparison.OrdinalIgnoreCase)) ?? PlayerThemes.DefaultName;
        OnPropertyChanged(nameof(CurrentTheme));
        CustomizationStatusText = snapshot.Errors.Count == 0 ? "Custom files loaded / カスタムを読み込みました" : string.Join(Environment.NewLine, snapshot.Errors);
    }

    public IReadOnlyList<string> InterfaceNames { get; } = ["Studio", "Classic"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStudioInterface))]
    [NotifyPropertyChangedFor(nameof(IsClassicSettingsVisible))]
    [NotifyPropertyChangedFor(nameof(ArtistColumnWidth))]
    [NotifyPropertyChangedFor(nameof(SidebarColumnWidth))]
    private string selectedInterfaceName = "Studio";

    public Avalonia.Controls.GridLength ArtistColumnWidth => IsStudioInterface ? new(1, Avalonia.Controls.GridUnitType.Star) : new(180);

    public bool IsStudioInterface => SelectedInterfaceName == "Studio";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeBackgroundTrack))]
    private bool useBeatmapBackground;

    /// <summary>Follow playback, or the selected track before playback starts.</summary>
    public TrackItemViewModel? ThemeBackgroundTrack => UseBeatmapBackground ? CurrentTrack ?? SelectedTrack : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentTheme))]
    private string selectedThemeName = PlayerThemes.DefaultName;

    /// <summary>Hex colour typed by the user; blank keeps the preset's accent.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentTheme))]
    [NotifyPropertyChangedFor(nameof(AccentColorStatusText))]
    private string accentColorText = string.Empty;

    public IReadOnlyList<string> ThemeNames => [.. PlayerThemes.Names, .. customization.Themes.Select(theme => theme.Name)];

    public PlayerTheme CurrentTheme
    {
        get
        {
            var theme = customization.Themes.FirstOrDefault(theme => theme.Name.Equals(SelectedThemeName, StringComparison.OrdinalIgnoreCase))
                ?? PlayerThemes.Resolve(SelectedThemeName, null);
            if (PlayerThemes.TryParseColor(AccentColorText, out var accent)) theme = theme.WithAccent(accent);
            if (UseBeatmapAccentColor && beatmapAccent is { } extracted)
                theme = theme.WithAccent(PlayerThemes.ReadableAccent(extracted, theme));
            return theme with { Ui = customization.Ui };
        }
    }

    public string AccentColorStatusText => UseBeatmapAccentColor && beatmapAccent is not null
        ? "Background image accent / 背景画像のテーマ色"
        : string.IsNullOrWhiteSpace(AccentColorText)
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
        SelectedInterfaceName = InterfaceNames.FirstOrDefault(name => string.Equals(name, settings.Appearance.InterfaceName, StringComparison.OrdinalIgnoreCase)) ?? "Studio";
        SelectedThemeName = ThemeNames.FirstOrDefault(name => string.Equals(name, settings.Appearance.ThemeName, StringComparison.OrdinalIgnoreCase)) ?? PlayerThemes.DefaultName;
        AccentColorText = PlayerThemes.TryParseColor(settings.Appearance.AccentColor, out _) ? settings.Appearance.AccentColor : string.Empty;
        UseBeatmapBackground = settings.Appearance.UseBeatmapBackground;
        UseBeatmapAccentColor = settings.Appearance.UseBeatmapAccentColor;
        applyTheme();
    }
}
