namespace OsuMusicPlayer.Core.Models;

public sealed record UnifiedBeatmap
{
    public required Guid Id { get; init; }

    public long? OnlineId { get; init; }

    public required string DifficultyName { get; init; }

    public OsuRuleset Ruleset { get; init; }

    /// <summary>MD5 of the .osu file as stored by osu!, used to resolve collections.</summary>
    public string? Md5Hash { get; init; }

    /// <summary>
    /// Full path of the .osu file, or <see langword="null"/> when it is not available.
    /// It is only opened on demand (video, storyboard), never during library loading.
    /// </summary>
    public string? BeatmapFilePath { get; init; }

    public double BPM { get; init; }

    public TimeSpan Length { get; init; }

    /// <summary>Song-select preview point. When the map defines none, osu! previews from 40% in.</summary>
    public TimeSpan PreviewTime { get; init; }

    public required string Tags { get; init; }

    /// <summary>No-mod star rating cached by the game, 0 when unknown.</summary>
    public double StarRating { get; init; }

    public double CircleSize { get; init; }

    public double ApproachRate { get; init; }

    public double DrainRate { get; init; }

    public double OverallDifficulty { get; init; }
}
