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

    /// <summary>The name the user last picked or the settings asked for, kept so a skin that shows up later (osu! found) is still selected.</summary>
    private string requestedPreviewSkinName = Core.Skins.PreviewSkin.DefaultName;
    private bool refreshingPreviewSkins;

    [ObservableProperty]
    private string selectedPreviewSkinName = Core.Skins.PreviewSkin.DefaultName;

    /// <summary>The loaded skin the preview window draws with; never null.</summary>
    [ObservableProperty]
    private PreviewSkin previewSkin = Core.Skins.PreviewSkin.Default;

    [ObservableProperty]
    private bool preferSkinComboColours;

    [ObservableProperty]
    private string previewSkinStatusText = string.Empty;

    /// <summary>"Default" followed by every skin folder found.</summary>
    public ObservableCollection<string> PreviewSkinNames { get; } = [Core.Skins.PreviewSkin.DefaultName];

    public string SkinsFolderPath => skinCatalog.RootPath;

    /// <summary>Re-scans the skin folders; call after adding a folder while the app is running.</summary>
    [RelayCommand]
    private void RefreshPreviewSkins()
    {
        skinCatalog.EnsureRootExists();
        refreshPreviewSkins();
    }

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

    partial void OnSelectedPreviewSkinNameChanged(string value)
    {
        if (refreshingPreviewSkins)
        {
            return;
        }

        requestedPreviewSkinName = string.IsNullOrWhiteSpace(value) ? Core.Skins.PreviewSkin.DefaultName : value;
        loadPreviewSkin();
    }

    private void applyRestoredPreviewSkin(AppSettings settings)
    {
        PreferSkinComboColours = settings.Appearance.PreferSkinComboColours;
        requestedPreviewSkinName = string.IsNullOrWhiteSpace(settings.Appearance.PreviewSkin) ? Core.Skins.PreviewSkin.DefaultName : settings.Appearance.PreviewSkin;
        skinCatalog.EnsureRootExists();
        refreshPreviewSkins();
    }

    /// <summary>Rebuilds the name list from the catalog and re-selects the requested skin if it is (now) present.</summary>
    private void refreshPreviewSkins()
    {
        var stableRoots = Installations
            .Select(static item => item.Installation)
            .Where(static installation => installation.Kind == OsuInstallationKind.Stable)
            .Select(static installation => installation.RootPath)
            .ToArray();
        skinEntries = skinCatalog.Enumerate(stableRoots);

        refreshingPreviewSkins = true;
        try
        {
            PreviewSkinNames.Clear();
            PreviewSkinNames.Add(Core.Skins.PreviewSkin.DefaultName);
            foreach (var entry in skinEntries)
            {
                PreviewSkinNames.Add(entry.Name);
            }

            var match = PreviewSkinCatalog.Find(skinEntries, requestedPreviewSkinName);
            SelectedPreviewSkinName = match?.Name ?? Core.Skins.PreviewSkin.DefaultName;
        }
        finally
        {
            refreshingPreviewSkins = false;
        }

        loadPreviewSkin();
    }

    private void loadPreviewSkin()
    {
        var entry = PreviewSkinCatalog.Find(skinEntries, SelectedPreviewSkinName);
        if (entry is null)
        {
            PreviewSkin = Core.Skins.PreviewSkin.Default;
            PreviewSkinStatusText = string.Equals(requestedPreviewSkinName, Core.Skins.PreviewSkin.DefaultName, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : $"Skin \"{requestedPreviewSkinName}\" was not found; using the default look.";
            return;
        }

        try
        {
            PreviewSkin = PreviewSkinLoader.Load(entry.Directory, entry.Name);
            PreviewSkinStatusText = string.Empty;
        }
        catch (DirectoryNotFoundException)
        {
            PreviewSkin = Core.Skins.PreviewSkin.Default;
            PreviewSkinStatusText = $"Skin folder \"{entry.Directory}\" disappeared; using the default look.";
        }
    }
}
