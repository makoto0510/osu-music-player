using System.Text.RegularExpressions;

namespace OsuMusicPlayer.Core.Loaders;

/// <summary>File names referenced from the [Events] section of a .osu file.</summary>
public sealed record BeatmapEventAssets(string? BackgroundFileName, string? VideoFileName, TimeSpan VideoOffset)
{
    public static BeatmapEventAssets Empty { get; } = new(null, null, TimeSpan.Zero);

    /// <summary>True when the .osu file itself declares storyboard sprites or animations.</summary>
    public bool HasStoryboardElements { get; init; }
}

/// <summary>
/// Minimal line-based reader for the parts of a .osu file the player needs. It never
/// parses hit objects, so damaged or unusual files cannot break library loading.
/// </summary>
public static partial class OsuBeatmapFileParser
{
    public static BeatmapEventAssets ReadEventAssets(string beatmapPath)
    {
        using var stream = new FileStream(beatmapPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        return ReadEventAssets(reader);
    }

    public static BeatmapEventAssets ReadEventAssets(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        string? background = null;
        string? video = null;
        var videoOffset = TimeSpan.Zero;
        var hasStoryboard = false;
        var inEvents = false;
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('['))
            {
                if (inEvents)
                {
                    break; // [Events] is finished; the rest of the file is irrelevant.
                }

                inEvents = trimmed.Equals("[Events]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inEvents || trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (!hasStoryboard &&
                (trimmed.StartsWith("Sprite,", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.StartsWith("Animation,", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.StartsWith("4,", StringComparison.Ordinal) ||
                 trimmed.StartsWith("6,", StringComparison.Ordinal)))
            {
                hasStoryboard = true;
                continue;
            }

            var match = EventPattern().Match(trimmed);
            if (!match.Success)
            {
                continue;
            }

            var kind = match.Groups["kind"].Value;
            var fileName = match.Groups["quoted"].Success
                ? match.Groups["quoted"].Value.Replace("\"\"", "\"", StringComparison.Ordinal)
                : match.Groups["plain"].Value.Trim();
            if (fileName.Length == 0)
            {
                continue;
            }

            if (kind is "0" or "Background")
            {
                background ??= fileName;
            }
            else if (kind is "1" or "Video")
            {
                if (video is null)
                {
                    video = fileName;
                    if (double.TryParse(match.Groups["time"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var offset))
                    {
                        videoOffset = TimeSpan.FromMilliseconds(offset);
                    }
                }
            }

            if (background is not null && video is not null && hasStoryboard)
            {
                break;
            }
        }

        return new BeatmapEventAssets(background, video, videoOffset) { HasStoryboardElements = hasStoryboard };
    }

    [GeneratedRegex("^(?<kind>0|1|Background|Video)\\s*,\\s*(?<time>-?[0-9.]+)\\s*,\\s*(?:\"(?<quoted>(?:\"\"|[^\"])*)\"|(?<plain>[^,]+))", RegexOptions.CultureInvariant)]
    private static partial Regex EventPattern();
}
