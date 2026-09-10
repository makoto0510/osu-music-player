using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.Core;

/// <summary>Selects one display entry per song without merging or modifying its beatmaps.</summary>
public static class SongDuplicateFilter
{
    public static IReadOnlyList<UnifiedBeatmapSet> Apply(IReadOnlyList<UnifiedBeatmapSet> sets)
    {
        var lengths = new Dictionary<(string Title, string Artist), SortedSet<double>>();
        var retained = new HashSet<Guid>();

        // Stable IDs make the representative independent of loader enumeration order.
        foreach (var set in sets.OrderBy(static set => set.Id))
        {
            var title = normalize(set.Title);
            var artist = normalize(set.Artist);
            var seconds = set.Beatmaps.Count == 0 ? 0 : set.Beatmaps.Max(static map => map.Length.TotalSeconds);
            if (title.Length == 0 || artist.Length == 0 || seconds <= 0)
            {
                retained.Add(set.Id);
                continue;
            }

            var key = (title, artist);
            if (!lengths.TryGetValue(key, out var representatives))
            {
                representatives = [];
                lengths.Add(key, representatives);
            }

            // Compare only with retained entries so a chain of near matches cannot
            // collapse songs whose durations differ by more than the tolerance.
            if (representatives.GetViewBetween(seconds - 2, seconds + 2).Count > 0)
            {
                continue;
            }

            representatives.Add(seconds);
            retained.Add(set.Id);
        }

        return sets.Where(set => retained.Contains(set.Id)).ToArray();
    }

    private static string normalize(string value) =>
        string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}
