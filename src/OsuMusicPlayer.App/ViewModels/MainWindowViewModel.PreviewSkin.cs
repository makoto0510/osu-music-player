using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OsuMusicPlayer.App.Services;
using OsuMusicPlayer.Core;
using OsuMusicPlayer.Core.Skins;

namespace OsuMusicPlayer.App.ViewModels;

/// <summary>
/// Skin selection for the difficulty preview. Skins are folders in the player's own Skins
/// folder (in Documents, so the user can drop files there) or in an osu!stable installation.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private readonly PreviewSkinCatalog skinCatalog;
    private IReadOnlyList<PreviewSkinEntry> skinEntries = [];

    /// <summary>
    /// The one piece of state: the name the user picked (or the settings asked for). The
    /// selection shown in the UI is derived from it, so a skin that only shows up later (an
    /// osu!stable install detected after start) is still selected once it appears.
    /// </summary>
    private string requestedPreviewSkinName = PreviewSkin.DefaultName;

    /// <summary>The loaded skin the preview window draws with; never null.</summary>
    [ObservableProperty]
    private PreviewSkin activePreviewSkin = PreviewSkin.Default;

    [ObservableProperty]
    private bool preferSkinComboColours;

    [ObservableProperty]
    private string previewSkinStatusText = string.Empty;

    /// <summary>"Default" followed by every skin folder found.</summary>
    public ObservableCollection<string> PreviewSkinNames { get; } = [PreviewSkin.DefaultName];

    /// <summary>The requested skin when it exists, otherwise "Default". Setting null (which the ComboBox does while its items are replaced) is ignored.</summary>
    public string SelectedPreviewSkinName
    {
        get => PreviewSkinCatalog.Find(skinEntries, requestedPreviewSkinName)?.Name ?? PreviewSkin.DefaultName;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || string.Equals(value, requestedPreviewSkinName, StringComparison.Ordinal))
            {
                return;
            }

            requestedPreviewSkinName = value;
            OnPropertyChanged();
            loadPreviewSkin();
        }
    }

    public string SkinsFolderPath => skinCatalog.RootPath;

    /// <summary>Re-scans the skin folders; call after adding a folder while the app is running.</summary>
    [RelayCommand]
    private void RefreshPreviewSkins() => refreshPreviewSkins();

    /// <summary>Creates the skins folder if needed and shows it in the file manager.</summary>
    [RelayCommand]
    private void OpenSkinsFolder()
    {
        if (!skinCatalog.EnsureRootExists())
        {
            PreviewSkinStatusText = $"Could not create {SkinsFolderPath}.";
            return;
        }

        if (!linkOpener.OpenFolder(SkinsFolderPath))
        {
            PreviewSkinStatusText = $"Skins folder: {SkinsFolderPath}";
        }
    }

    private void applyRestoredPreviewSkin(AppSettings settings)
    {
        PreferSkinComboColours = settings.Appearance.PreferSkinComboColours;
        requestedPreviewSkinName = string.IsNullOrWhiteSpace(settings.Appearance.PreviewSkin) ? PreviewSkin.DefaultName : settings.Appearance.PreviewSkin;
        refreshPreviewSkins();
    }

    /// <summary>Rebuilds the name list from the catalog; the selection follows <see cref="requestedPreviewSkinName"/>.</summary>
    private void refreshPreviewSkins()
    {
        skinCatalog.EnsureRootExists();
        var stableRoots = Installations
            .Select(static item => item.Installation)
            .Where(static installation => installation.Kind == OsuInstallationKind.Stable)
            .Select(static installation => installation.RootPath);
        var previousSelection = SelectedPreviewSkinName;
        skinEntries = skinCatalog.Enumerate(stableRoots);

        PreviewSkinNames.Clear();
        PreviewSkinNames.Add(PreviewSkin.DefaultName);
        foreach (var entry in skinEntries)
        {
            PreviewSkinNames.Add(entry.Name);
        }

        if (!string.Equals(previousSelection, SelectedPreviewSkinName, StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(SelectedPreviewSkinName));
        }

        loadPreviewSkin();
    }

    private void loadPreviewSkin()
    {
        var entry = PreviewSkinCatalog.Find(skinEntries, requestedPreviewSkinName);
        if (entry is null)
        {
            ActivePreviewSkin = PreviewSkin.Default;
            PreviewSkinStatusText = string.Equals(requestedPreviewSkinName, PreviewSkin.DefaultName, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : $"Skin \"{requestedPreviewSkinName}\" was not found; using the default look.";
            return;
        }

        if (string.Equals(ActivePreviewSkin.Directory, entry.Directory, StringComparison.Ordinal))
        {
            return; // same folder: keep the loaded skin (and the preview's decoded sprites)
        }

        try
        {
            ActivePreviewSkin = PreviewSkinLoader.Load(entry.Directory, entry.Name);
            PreviewSkinStatusText = string.Empty;
        }
        catch (DirectoryNotFoundException)
        {
            ActivePreviewSkin = PreviewSkin.Default;
            PreviewSkinStatusText = $"Skin folder \"{entry.Directory}\" disappeared; using the default look.";
        }
    }
}
