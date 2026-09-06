using System.Globalization;
using OsuMusicPlayer.Core.Preview;

namespace OsuMusicPlayer.Core.Skins;

/// <summary>Reads a skin folder's skin.ini (osu!'s INI dialect) into a <see cref="PreviewSkin"/>.</summary>
public static class PreviewSkinLoader
{
    public const string IniFileName = "skin.ini";

    /// <summary>
    /// Loads the skin stored in <paramref name="directory"/>. A folder without skin.ini is a
    /// valid skin that only overrides images; a missing folder throws <see cref="DirectoryNotFoundException"/>.
    /// </summary>
    public static PreviewSkin Load(string directory, string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Skin folder not found: {directory}");
        }

        var iniPath = Path.Combine(directory, IniFileName);
        var fallbackName = string.IsNullOrWhiteSpace(displayName) ? new DirectoryInfo(directory).Name : displayName;
        if (!File.Exists(iniPath))
        {
            return new PreviewSkin(fallbackName, directory);
        }

        try
        {
            return Parse(File.ReadLines(iniPath), directory, fallbackName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A locked or unreadable skin.ini still leaves the images usable.
            return new PreviewSkin(fallbackName, directory);
        }
    }

    /// <summary>Parses skin.ini text. <paramref name="directory"/> may be null for tests that only care about the values.</summary>
    public static PreviewSkin Parse(IEnumerable<string> lines, string? directory, string fallbackName)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackName);

        var name = string.Empty;
        var author = string.Empty;
        var combos = new SortedDictionary<int, PreviewComboColour>();
        PreviewComboColour? sliderBorder = null;
        PreviewComboColour? sliderTrack = null;
        PreviewComboColour? sliderBall = null;
        var prefix = "default";
        var overlap = -2;
        var allowSliderBallTint = false;
        var cursorCentre = true;
        var mania = new List<ManiaSkinColours>();
        var section = string.Empty;
        ManiaSection? currentMania = null;

        foreach (var rawLine in lines)
        {
            var line = stripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                flushMania();
                section = line[1..^1].Trim().ToUpperInvariant();
                if (section == "MANIA")
                {
                    currentMania = new ManiaSection();
                }

                continue;
            }

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            switch (section)
            {
                case "GENERAL":
                    if (key.Equals("Name", StringComparison.OrdinalIgnoreCase))
                    {
                        name = value;
                    }
                    else if (key.Equals("Author", StringComparison.OrdinalIgnoreCase))
                    {
                        author = value;
                    }
                    else if (key.Equals("AllowSliderBallTint", StringComparison.OrdinalIgnoreCase))
                    {
                        allowSliderBallTint = parseBool(value, allowSliderBallTint);
                    }
                    else if (key.Equals("CursorCentre", StringComparison.OrdinalIgnoreCase))
                    {
                        cursorCentre = parseBool(value, cursorCentre);
                    }

                    break;
                case "COLOURS":
                    if (!TryParseColour(value, out var colour))
                    {
                        break;
                    }

                    if (key.StartsWith("Combo", StringComparison.OrdinalIgnoreCase) && int.TryParse(key.AsSpan(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                    {
                        combos[index] = colour;
                    }
                    else
                    {
                        switch (key.ToUpperInvariant())
                        {
                            case "SLIDERBORDER":
                                sliderBorder = colour;
                                break;
                            case "SLIDERTRACKOVERRIDE":
                                sliderTrack = colour;
                                break;
                            case "SLIDERBALL":
                                sliderBall = colour;
                                break;
                        }
                    }

                    break;
                case "FONTS":
                    if (key.Equals("HitCirclePrefix", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                    {
                        // Prefixes may point into a sub-folder ("fonts/hitcircle"); osu! accepts either slash.
                        prefix = value.Replace('\\', '/');
                    }
                    else if (key.Equals("HitCircleOverlap", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedOverlap))
                    {
                        overlap = parsedOverlap;
                    }

                    break;
                case "MANIA" when currentMania is not null:
                    if (key.Equals("Keys", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var keys))
                    {
                        currentMania.Keys = keys;
                    }
                    else if (key.StartsWith("Colour", StringComparison.OrdinalIgnoreCase)
                             && int.TryParse(key.AsSpan(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out var column)
                             && TryParseColour(value, out var columnColour))
                    {
                        currentMania.Columns[column] = columnColour;
                    }
                    else if (key.Equals("ColourHold", StringComparison.OrdinalIgnoreCase) && TryParseColour(value, out var hold))
                    {
                        currentMania.Hold = hold;
                    }

                    break;
            }
        }

        flushMania();
        return new PreviewSkin(
            string.IsNullOrWhiteSpace(name) ? fallbackName : name.Trim(),
            directory,
            author,
            combos.Values.ToArray(),
            sliderBorder,
            sliderTrack,
            sliderBall,
            prefix,
            overlap,
            allowSliderBallTint,
            cursorCentre,
            mania);

        void flushMania()
        {
            if (currentMania is { Keys: > 0 } finished && !mania.Any(existing => existing.Keys == finished.Keys))
            {
                mania.Add(new ManiaSkinColours(finished.Keys, finished.Columns.Values.ToArray(), finished.Hold));
            }

            currentMania = null;
        }
    }

    /// <summary>Accepts "r,g,b" and "r,g,b,a" (alpha ignored), each 0..255, with any spacing.</summary>
    public static bool TryParseColour(string? text, out PreviewComboColour colour)
    {
        colour = new PreviewComboColour(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is not (3 or 4))
        {
            return false;
        }

        var channels = new byte[3];
        for (var i = 0; i < 3; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            channels[i] = (byte)Math.Clamp(Math.Round(value), 0, 255);
        }

        colour = new PreviewComboColour(channels[0], channels[1], channels[2]);
        return true;
    }

    private static bool parseBool(string value, bool fallback) => value.Trim() switch
    {
        "1" => true,
        "0" => false,
        _ when bool.TryParse(value, out var parsed) => parsed,
        _ => fallback,
    };

    private static string stripComment(string line)
    {
        var comment = line.IndexOf("//", StringComparison.Ordinal);
        return comment < 0 ? line : line[..comment];
    }

    private sealed class ManiaSection
    {
        public int Keys { get; set; }

        public SortedDictionary<int, PreviewComboColour> Columns { get; } = [];

        public PreviewComboColour? Hold { get; set; }
    }
}
