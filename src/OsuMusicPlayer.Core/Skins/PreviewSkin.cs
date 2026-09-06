using OsuMusicPlayer.Core.Preview;

namespace OsuMusicPlayer.Core.Skins;

/// <summary>One image file of a skin; <see cref="Scale"/> is 2 for an "@2x" asset, otherwise 1.</summary>
public sealed record SkinImage(string Path, float Scale);

/// <summary>Column colours for one key count, from a <c>[Mania]</c> section of skin.ini.</summary>
public sealed record ManiaSkinColours(int Keys, IReadOnlyList<PreviewComboColour> Columns, PreviewComboColour? Hold);

/// <summary>
/// An osu!-style skin folder as far as the difficulty preview cares: the colours and fonts
/// declared in skin.ini plus a lookup for the PNG elements. Any element that is missing is
/// drawn with the built-in vector look, so a skin can override as little as one file.
/// </summary>
public sealed class PreviewSkin
{
    public const string DefaultName = "Default";

    private static readonly string[] image_extensions = [".png", ".jpg", ".jpeg"];
    private static readonly (string Suffix, float Scale)[] resolutions = [("@2x", 2f), ("", 1f)];
    private static readonly string[] frame_separators = ["-", string.Empty];

    /// <summary>Relative path (forward slashes, case-insensitive) to full path, listed once per skin.</summary>
    private readonly Lazy<Dictionary<string, string>> files;

    public PreviewSkin(
        string name,
        string? directory,
        string author = "",
        IReadOnlyList<PreviewComboColour>? comboColours = null,
        PreviewComboColour? sliderBorder = null,
        PreviewComboColour? sliderTrackOverride = null,
        PreviewComboColour? sliderBall = null,
        string hitCirclePrefix = "default",
        int hitCircleOverlap = -2,
        bool allowSliderBallTint = false,
        bool cursorCentre = true,
        IReadOnlyList<ManiaSkinColours>? mania = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Directory = string.IsNullOrWhiteSpace(directory) ? null : System.IO.Path.GetFullPath(directory);
        Author = author;
        ComboColours = comboColours ?? [];
        SliderBorder = sliderBorder;
        SliderTrackOverride = sliderTrackOverride;
        SliderBall = sliderBall;
        HitCirclePrefix = string.IsNullOrWhiteSpace(hitCirclePrefix) ? "default" : hitCirclePrefix.Trim();
        HitCircleOverlap = hitCircleOverlap;
        AllowSliderBallTint = allowSliderBallTint;
        CursorCentre = cursorCentre;
        Mania = mania ?? [];
        files = new Lazy<Dictionary<string, string>>(listFiles, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The built-in look: no folder, no images, every element drawn as vectors.</summary>
    public static PreviewSkin Default { get; } = new(DefaultName, null);

    public string Name { get; }

    /// <summary>Folder holding skin.ini and the images; null for <see cref="Default"/>.</summary>
    public string? Directory { get; }

    public string Author { get; }

    public bool IsDefault => Directory is null;

    /// <summary>Combo1..ComboN from skin.ini, in order; empty when the skin declares none.</summary>
    public IReadOnlyList<PreviewComboColour> ComboColours { get; }

    public PreviewComboColour? SliderBorder { get; }

    public PreviewComboColour? SliderTrackOverride { get; }

    public PreviewComboColour? SliderBall { get; }

    /// <summary>Prefix of the combo number images, e.g. "default" for default-0.png … default-9.png.</summary>
    public string HitCirclePrefix { get; }

    /// <summary>Pixels (at 1x) that neighbouring digits overlap; negative spreads them apart.</summary>
    public int HitCircleOverlap { get; }

    public bool AllowSliderBallTint { get; }

    public bool CursorCentre { get; }

    public IReadOnlyList<ManiaSkinColours> Mania { get; }

    public ManiaSkinColours? ManiaFor(int keys)
    {
        foreach (var section in Mania)
        {
            if (section.Keys == keys)
            {
                return section;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds an element by its osu! name (without extension), preferring the "@2x" version.
    /// Returns null when the skin has no such file, which tells the renderer to draw vectors.
    /// </summary>
    public SkinImage? FindImage(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (Directory is null)
        {
            return null;
        }

        var index = files.Value;
        foreach (var (suffix, scale) in resolutions)
        {
            foreach (var extension in image_extensions)
            {
                if (index.TryGetValue(name + suffix + extension, out var path))
                {
                    return new SkinImage(path, scale);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The frames of an animated element: name-0, name-1, … (osu!'s hyphenated form) or name0,
    /// name1, …; a single non-animated file counts as one frame. Empty when nothing exists.
    /// </summary>
    public IReadOnlyList<SkinImage> FindAnimation(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        foreach (var separator in frame_separators)
        {
            var frames = new List<SkinImage>();
            for (var index = 0; FindImage($"{name}{separator}{index}") is { } frame; index++)
            {
                frames.Add(frame);
            }

            if (frames.Count > 0)
            {
                return frames;
            }
        }

        return FindImage(name) is { } single ? [single] : [];
    }

    private Dictionary<string, string> listFiles()
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Directory is null)
        {
            return index;
        }

        try
        {
            // Sub-folders are included because HitCirclePrefix may point into one ("fonts/hitcircle").
            foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*", SearchOption.AllDirectories))
            {
                var relative = System.IO.Path.GetRelativePath(Directory, path).Replace('\\', '/');
                index.TryAdd(relative, path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An unreadable folder is an empty skin; the vector look takes over.
        }

        return index;
    }
}
