using System.Globalization;
using System.Text;
using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.Core.Search;

/// <summary>
/// The player's search language. Words match any metadata field; <c>artist:</c>,
/// <c>title:</c>, <c>mapper:</c>, <c>tag:</c>, <c>source:</c>, <c>mode:</c>, <c>bpm:</c>,
/// <c>stars:</c> and <c>length:</c> restrict a term to one field; <c>bpm:120-180</c>
/// style ranges are accepted for numbers; <c>-term</c> excludes; <c>a|b</c> means either;
/// quotes keep spaces together (<c>artist:"Camellia feat. Nanahira"</c>).
/// </summary>
public sealed class TrackSearchQuery
{
    private readonly IReadOnlyList<Term> terms;

    private TrackSearchQuery(IReadOnlyList<Term> terms)
    {
        this.terms = terms;
    }

    public static TrackSearchQuery Empty { get; } = new([]);

    public bool IsEmpty => terms.Count == 0;

    public static TrackSearchQuery Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Empty;
        }

        var terms = new List<Term>();
        foreach (var token in tokenize(text))
        {
            var negated = token.Length > 1 && token[0] == '-';
            var body = negated ? token[1..] : token;
            if (body.Length == 0)
            {
                continue;
            }

            var field = Field.Any;
            var extraAlternatives = Array.Empty<string>();
            var colon = body.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0 && colon + 1 < body.Length)
            {
                if (tryParseField(body[..colon], out var parsedField))
                {
                    field = parsedField;
                    body = body[(colon + 1)..];
                }
                else
                {
                    // "re:zero" or a mistyped key: match the whole token or just the part after the colon.
                    extraAlternatives = body[(colon + 1)..].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                }
            }

            var alternatives = body.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Concat(extraAlternatives).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (alternatives.Length == 0)
            {
                continue;
            }

            terms.Add(new Term(field, alternatives, negated));
        }

        return terms.Count == 0 ? Empty : new TrackSearchQuery(terms);
    }

    public bool Matches(UnifiedBeatmapSet set) => Matches(set, null, null);

    /// <summary>Matches with optional online metadata (genre and language are not stored locally by osu!).</summary>
    public bool Matches(UnifiedBeatmapSet set, string? genre, string? language)
    {
        ArgumentNullException.ThrowIfNull(set);
        foreach (var term in terms)
        {
            var matched = term.Alternatives.Any(alternative => term.Field switch
            {
                Field.Genre => contains(genre, alternative),
                Field.Language => contains(language, alternative),
                _ => matches(set, term.Field, alternative),
            });
            if (matched == term.Negated)
            {
                return false;
            }
        }

        return true;
    }

    private static bool matches(UnifiedBeatmapSet set, Field field, string value) => field switch
    {
        Field.Artist => contains(set.Artist, value) || contains(set.ArtistUnicode, value),
        Field.Title => contains(set.Title, value) || contains(set.TitleUnicode, value),
        Field.Mapper => contains(set.Creator, value),
        Field.Tag => set.Beatmaps.Any(beatmap => beatmap.Tags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(tag => tag.Equals(value, StringComparison.OrdinalIgnoreCase))) || set.Beatmaps.Any(beatmap => contains(beatmap.Tags, value)),
        Field.Difficulty => set.Beatmaps.Any(beatmap => contains(beatmap.DifficultyName, value)),
        Field.Source => matchesSource(set.Source, value),
        Field.Mode => set.Beatmaps.Any(beatmap => matchesRuleset(beatmap.Ruleset, value)),
        Field.Bpm => set.Beatmaps.Any(beatmap => inRange(beatmap.BPM, value)),
        Field.Stars => set.Beatmaps.Any(beatmap => inRange(beatmap.StarRating, value)),
        Field.Length => set.Beatmaps.Any(beatmap => inRange(beatmap.Length.TotalSeconds, value, parseLength)),
        _ => contains(set.Title, value) || contains(set.TitleUnicode, value) ||
             contains(set.Artist, value) || contains(set.ArtistUnicode, value) ||
             contains(set.Creator, value) ||
             set.Beatmaps.Any(beatmap => contains(beatmap.Tags, value) || contains(beatmap.DifficultyName, value)),
    };

    private static bool contains(string? haystack, string needle) =>
        !string.IsNullOrEmpty(haystack) && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static bool matchesSource(BeatmapSource source, string value) => value.ToLowerInvariant() switch
    {
        "stable" => source is BeatmapSource.Stable or BeatmapSource.Both,
        "lazer" => source is BeatmapSource.Lazer or BeatmapSource.Both,
        "both" => source == BeatmapSource.Both,
        _ => false,
    };

    private static bool matchesRuleset(OsuRuleset ruleset, string value) => value.ToLowerInvariant() switch
    {
        "osu" or "std" or "standard" or "0" => ruleset == OsuRuleset.Osu,
        "taiko" or "1" => ruleset == OsuRuleset.Taiko,
        "catch" or "ctb" or "fruits" or "2" => ruleset == OsuRuleset.Catch,
        "mania" or "3" => ruleset == OsuRuleset.Mania,
        _ => false,
    };

    private static bool inRange(double actual, string value) => inRange(actual, value, parseNumber);

    private static bool inRange(double actual, string value, Func<string, double?> parse)
    {
        if (value.StartsWith(">=", StringComparison.Ordinal) && parse(value[2..]) is { } min1)
        {
            return actual >= min1;
        }

        if (value.StartsWith("<=", StringComparison.Ordinal) && parse(value[2..]) is { } max1)
        {
            return actual <= max1;
        }

        if (value.StartsWith('>') && parse(value[1..]) is { } min2)
        {
            return actual > min2;
        }

        if (value.StartsWith('<') && parse(value[1..]) is { } max2)
        {
            return actual < max2;
        }

        var dash = value.IndexOf('-', 1);
        if (dash > 0)
        {
            var low = parse(value[..dash]);
            var high = parse(value[(dash + 1)..]);
            return low is not null && high is not null && actual >= low && actual <= high;
        }

        if (parse(value) is { } exact)
        {
            // A single number matches a small window around it (BPM 180 also matches 180.4).
            return Math.Abs(actual - exact) < 0.5 + exact * 0.005;
        }

        return false;
    }

    private static double? parseNumber(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? number : null;

    private static double? parseLength(string text)
    {
        var parts = text.Split(':');
        if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return minutes * 60 + seconds;
        }

        return parseNumber(text);
    }

    private static bool tryParseField(string name, out Field field)
    {
        field = name.ToLowerInvariant() switch
        {
            "artist" => Field.Artist,
            "title" => Field.Title,
            "mapper" or "creator" or "mapped" => Field.Mapper,
            "tag" or "tags" => Field.Tag,
            "diff" or "difficulty" or "version" => Field.Difficulty,
            "source" or "from" => Field.Source,
            "mode" or "ruleset" => Field.Mode,
            "bpm" => Field.Bpm,
            "stars" or "star" or "sr" => Field.Stars,
            "length" or "duration" => Field.Length,
            "genre" => Field.Genre,
            "language" or "lang" => Field.Language,
            _ => Field.Any,
        };
        return field != Field.Any;
    }

    private static IEnumerable<string> tokenize(string text)
    {
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var character in text)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private enum Field
    {
        Any,
        Artist,
        Title,
        Mapper,
        Tag,
        Difficulty,
        Source,
        Mode,
        Bpm,
        Stars,
        Length,
        Genre,
        Language,
    }

    private sealed record Term(Field Field, string[] Alternatives, bool Negated);
}
