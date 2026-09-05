using OsuMusicPlayer.Core.Loaders;
using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.Core;

/// <summary>Extra media of a beatmap set that is only needed while it plays.</summary>
public sealed record BeatmapMedia(string? VideoFilePath, TimeSpan VideoOffset, string? StoryboardFilePath)
{
    public static BeatmapMedia None { get; } = new(null, TimeSpan.Zero, null);

    public bool HasVideo => VideoFilePath is not null;

    public bool HasStoryboard => StoryboardFilePath is not null;
}

/// <summary>
/// Finds the background video and storyboard of a set on demand, by reading the
/// [Events] section of its .osu files. Loading this for every set up front would
/// mean opening tens of thousands of files, so the player asks per track instead.
/// </summary>
public sealed class BeatmapMediaResolver : IBeatmapMediaResolver
{
    public Task<BeatmapMedia> ResolveAsync(UnifiedBeatmapSet set, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(set);
        return Task.Run(() => resolve(set, cancellationToken), cancellationToken);
    }

    private static BeatmapMedia resolve(UnifiedBeatmapSet set, CancellationToken cancellationToken)
    {
        var files = set.Files;
        if (files is null)
        {
            return BeatmapMedia.None;
        }

        string? videoPath = null;
        var videoOffset = TimeSpan.Zero;
        foreach (var beatmap in set.Beatmaps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (beatmap.BeatmapFilePath is null)
            {
                continue;
            }

            try
            {
                var assets = OsuBeatmapFileParser.ReadEventAssets(beatmap.BeatmapFilePath);
                videoPath = files.Resolve(assets.VideoFileName);
                if (videoPath is not null)
                {
                    videoOffset = assets.VideoOffset;
                    break;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // A broken .osu file must not hide the media of the other difficulties.
            }
        }

        string? storyboardPath = null;
        try
        {
            var storyboardName = files.FileNames.FirstOrDefault(static name => name.EndsWith(".osb", StringComparison.OrdinalIgnoreCase));
            storyboardPath = files.Resolve(storyboardName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return new BeatmapMedia(videoPath, videoOffset, storyboardPath);
    }
}
