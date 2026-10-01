using System.Runtime.InteropServices;

namespace OsuMusicPlayer.Core.Skins;

/// <summary>A skin folder the user can pick; <see cref="Name"/> is what the UI shows and what settings store.</summary>
public sealed record PreviewSkinEntry(string Name, string Directory);

/// <summary>
/// Lists the skins available to the difficulty preview: every sub-folder of the player's own
/// Skins folder plus an optional Documents folder, then (on Windows only) the read-only
/// Skins folders of any osu!stable installation that was passed in.
/// </summary>
public sealed class PreviewSkinCatalog
{
    public const string ReadMeFileName = "README.txt";
    private const string stable_prefix = "osu!stable: ";
    private const string documents_prefix = "Documents: ";

    public PreviewSkinCatalog(string rootPath, string? additionalRootPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        RootPath = Path.GetFullPath(rootPath);
        AdditionalRootPath = string.IsNullOrWhiteSpace(additionalRootPath) ? null : Path.GetFullPath(additionalRootPath);
    }

    /// <summary>The player's own skins folder, e.g. <c>Documents/OsuMusicPlayer/Skins</c>.</summary>
    public string RootPath { get; }

    /// <summary>An optional secondary skin folder, typically the user's Documents skins.</summary>
    public string? AdditionalRootPath { get; }

    /// <summary>A catalog rooted at <see cref="GetDefaultRootPath"/>.</summary>
    public static PreviewSkinCatalog CreateDefault() => new(GetDefaultRootPath());

    /// <summary>The folder the player uses by default: the user's Documents folder, so it is easy to find in a file manager.</summary>
    public static string GetDefaultRootPath()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments, Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(documents))
        {
            documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);
        }

        if (string.IsNullOrWhiteSpace(documents))
        {
            documents = Path.GetTempPath();
        }

        return Path.Combine(documents, "OsuMusicPlayer", "Skins");
    }

    /// <summary>
    /// Creates the skins folder and drops a README explaining the layout, so a user who opens
    /// the folder knows what to put there. Returns false when the folder could not be created.
    /// </summary>
    public bool EnsureRootExists()
    {
        try
        {
            Directory.CreateDirectory(RootPath);
            var readMe = Path.Combine(RootPath, ReadMeFileName);
            if (!File.Exists(readMe))
            {
                File.WriteAllText(readMe, ReadMeText);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Enumerates primary folders, prefixed secondary folders, then Windows osu!stable folders.
    /// </summary>
    public IReadOnlyList<PreviewSkinEntry> Enumerate(IEnumerable<string>? stableRoots = null)
    {
        var entries = new List<PreviewSkinEntry>();
        foreach (var directory in listSkinFolders(RootPath))
        {
            entries.Add(new PreviewSkinEntry(Path.GetFileName(directory), directory));
        }

        if (AdditionalRootPath is not null && !string.Equals(RootPath, AdditionalRootPath, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var directory in listSkinFolders(AdditionalRootPath))
            {
                entries.Add(new PreviewSkinEntry(documents_prefix + Path.GetFileName(directory), directory));
            }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            foreach (var stableRoot in stableRoots ?? [])
            {
                if (string.IsNullOrWhiteSpace(stableRoot))
                {
                    continue;
                }

                foreach (var directory in listSkinFolders(Path.Combine(stableRoot, "Skins")))
                {
                    entries.Add(new PreviewSkinEntry(stable_prefix + Path.GetFileName(directory), directory));
                }
            }
        }

        return entries;
    }

    /// <summary>Finds the entry whose name matches (case-insensitively); null for the default skin or an unknown name.</summary>
    public static PreviewSkinEntry? Find(IEnumerable<PreviewSkinEntry> entries, string? name)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (string.IsNullOrWhiteSpace(name) || name.Equals(PreviewSkin.DefaultName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var exact = entries.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        // Settings stored the old unprefixed folder name. Prefer a primary-root match above,
        // then migrate the selection to the secondary Documents entry if needed.
        return entries.FirstOrDefault(entry => string.Equals(entry.Name, documents_prefix + name, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> listSkinFolders(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateDirectories(root)
                .Where(static directory => !Path.GetFileName(directory).StartsWith('.'))
                .OrderBy(static directory => Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static string ReadMeText { get; } = """
        osu! Music Player - difficulty preview skins
        ============================================

        Put one folder per skin in here, then pick it in Settings > Appearance (or in the
        preview window's Skin box). A folder is a skin as soon as it exists; every file in it
        is optional and anything missing is drawn with the built-in look. You can also copy an
        osu!stable skin folder here as-is, and skins installed in osu!stable are listed too.

        My skin/
          skin.ini                 optional; osu!'s format:
                                   [General]  Name, Author, AllowSliderBallTint, CursorCentre
                                   [Colours]  Combo1..Combo8, SliderBorder, SliderTrackOverride, SliderBall
                                   [Fonts]    HitCirclePrefix, HitCircleOverlap
                                   [Mania]    Keys, Colour1..ColourN, ColourHold
          hitcircle.png            tinted with the combo colour
          hitcircleoverlay.png
          approachcircle.png
          default-0.png .. default-9.png   combo numbers (HitCirclePrefix changes the "default" part)
          sliderb.png / sliderb0.png ..    slider ball (frames animate)
          sliderfollowcircle.png
          reversearrow.png
          spinner-circle.png
          cursor.png
          taikohitcircle.png, taikohitcircleoverlay.png, taikobigcircle.png, taikobigcircleoverlay.png
          taiko-roll-middle.png, taiko-roll-end.png
          mania-note1.png, mania-note2.png, mania-noteS.png   (+ -note1H / -note1L / -note1T for holds)
          fruit-apple.png, fruit-grapes.png, fruit-orange.png, fruit-pear.png (+ -overlay), fruit-drop.png,
          fruit-bananas.png, fruit-catcher-idle.png

        "@2x" files (e.g. hitcircle@2x.png) are preferred over the 1x file when both exist.
        Use "Refresh" in Settings after adding a folder.
        """;
}
