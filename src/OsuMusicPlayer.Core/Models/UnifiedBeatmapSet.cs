namespace OsuMusicPlayer.Core.Models;

public sealed record UnifiedBeatmapSet
{
    public required Guid Id { get; init; }

    public long? OnlineId { get; init; }

    public required string Title { get; init; }

    public required string TitleUnicode { get; init; }

    public required string Artist { get; init; }

    public required string ArtistUnicode { get; init; }

    public required string Creator { get; init; }

    public string? AudioFilePath { get; init; }

    public string? BackgroundFilePath { get; init; }

    public BeatmapSource Source { get; init; }

    /// <summary>Resolves the other files of the set (video, storyboard, samples).</summary>
    public IBeatmapFileResolver? Files { get; init; }

    public required IReadOnlyList<UnifiedBeatmap> Beatmaps { get; init; }
}
