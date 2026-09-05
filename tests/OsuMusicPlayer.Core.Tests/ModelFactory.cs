using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.Core.Tests;

internal static class ModelFactory
{
    public static UnifiedBeatmapSet Set(
        long? onlineId = null,
        string artist = "Artist",
        string title = "Title",
        string creator = "Creator",
        BeatmapSource source = BeatmapSource.Stable,
        params UnifiedBeatmap[] beatmaps) => new()
        {
            Id = Guid.NewGuid(),
            OnlineId = onlineId,
            Title = title,
            TitleUnicode = title,
            Artist = artist,
            ArtistUnicode = artist,
            Creator = creator,
            Source = source,
            Beatmaps = beatmaps,
        };

    public static UnifiedBeatmap Beatmap(long? onlineId = null, string difficulty = "Hard") => new()
    {
        Id = Guid.NewGuid(),
        OnlineId = onlineId,
        DifficultyName = difficulty,
        Tags = string.Empty,
    };
}
