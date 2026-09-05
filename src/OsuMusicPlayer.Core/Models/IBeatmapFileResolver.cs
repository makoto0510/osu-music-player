namespace OsuMusicPlayer.Core.Models;

/// <summary>
/// Maps the file names referenced inside a beatmap (video, storyboard, samples) to
/// their location on disk. osu!stable keeps them in the song folder, osu!lazer in the
/// hash-addressed <c>files</c> store, so the set carries a resolver instead of paths.
/// </summary>
public interface IBeatmapFileResolver
{
    /// <summary>All file names that belong to the set, as referenced by beatmaps.</summary>
    IEnumerable<string> FileNames { get; }

    /// <summary>Returns the full path for a referenced file name, or <see langword="null"/> if it does not exist.</summary>
    string? Resolve(string? fileName);
}
