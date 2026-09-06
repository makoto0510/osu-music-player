using OsuMusicPlayer.App.Services;

namespace OsuMusicPlayer.App.ViewModels;

/// <summary>
/// Content-based recommendations: the tags, artists, mappers and tempo of what the user
/// plays most build a taste profile; unplayed tracks are ranked by how well they fit it.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private const int recommendation_count = 50;
    private const int minimum_history_for_recommendations = 3;

    private readonly Dictionary<Guid, PlayHistoryEntry> playHistory = [];

    private bool recommendationsAvailable => playHistory.Count >= minimum_history_for_recommendations;

    public IReadOnlyList<PlayHistoryEntry> PlayHistorySnapshot => playHistory.Values.OrderByDescending(static entry => entry.LastPlayedUtc).ToArray();

    private void recordPlay(TrackItemViewModel track)
    {
        var id = track.Model.Id;
        var previous = playHistory.GetValueOrDefault(id);
        playHistory[id] = new PlayHistoryEntry(id, (previous?.PlayCount ?? 0) + 1, DateTime.UtcNow);
        if (playHistory.Count == minimum_history_for_recommendations)
        {
            rebuildViews();
        }
    }

    private void applyRestoredHistory(AppSettings settings)
    {
        playHistory.Clear();
        foreach (var entry in settings.PlayHistory)
        {
            if (entry.PlayCount > 0)
            {
                playHistory[entry.TrackId] = entry;
            }
        }
    }

    /// <summary>Orders unplayed tracks by similarity to the taste profile; played tracks come last.</summary>
    private IEnumerable<TrackItemViewModel> recommendTracks(IEnumerable<TrackItemViewModel> tracks)
    {
        var profile = TasteProfile.Build(playHistory, tracksById);
        return tracks
            .Where(track => !playHistory.ContainsKey(track.Model.Id))
            .Select(track => (Track: track, Score: profile.Score(track)))
            .Where(static pair => pair.Score > 0)
            .OrderByDescending(static pair => pair.Score)
            .ThenBy(static pair => pair.Track.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(recommendation_count)
            .Select(static pair => pair.Track);
    }

    /// <summary>Weighted counts of what the listener played; weights decay with time since last play.</summary>
    internal sealed class TasteProfile
    {
        private readonly Dictionary<string, double> tags = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> artists = new(StringComparer.CurrentCultureIgnoreCase);
        private readonly Dictionary<string, double> mappers = new(StringComparer.CurrentCultureIgnoreCase);
        private double bpmSum;
        private double bpmWeight;

        public static TasteProfile Build(IReadOnlyDictionary<Guid, PlayHistoryEntry> history, IReadOnlyDictionary<Guid, TrackItemViewModel> tracks)
        {
            var profile = new TasteProfile();
            var now = DateTime.UtcNow;
            foreach (var entry in history.Values)
            {
                if (!tracks.TryGetValue(entry.TrackId, out var track))
                {
                    continue;
                }

                var age = Math.Max(0, (now - entry.LastPlayedUtc).TotalDays);
                var weight = Math.Log2(1 + entry.PlayCount) * Math.Exp(-age / 60d);
                foreach (var tag in track.Tags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    profile.tags[tag] = profile.tags.GetValueOrDefault(tag) + weight;
                }

                profile.artists[track.Artist] = profile.artists.GetValueOrDefault(track.Artist) + weight;
                profile.mappers[track.Creator] = profile.mappers.GetValueOrDefault(track.Creator) + weight;
                if (track.BPM > 0)
                {
                    profile.bpmSum += track.BPM * weight;
                    profile.bpmWeight += weight;
                }
            }

            return profile;
        }

        public double Score(TrackItemViewModel track)
        {
            var score = 0d;
            var trackTags = track.Tags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var tag in trackTags)
            {
                score += tags.GetValueOrDefault(tag);
            }

            if (trackTags.Length > 0)
            {
                score /= Math.Sqrt(trackTags.Length);
            }

            score += artists.GetValueOrDefault(track.Artist) * 3;
            score += mappers.GetValueOrDefault(track.Creator) * 1.5;
            if (bpmWeight > 0 && track.BPM > 0)
            {
                var averageBpm = bpmSum / bpmWeight;
                var distance = Math.Abs(track.BPM - averageBpm) / Math.Max(1, averageBpm);
                score += Math.Max(0, 1 - distance * 2) * 0.5;
            }

            return score;
        }
    }
}
