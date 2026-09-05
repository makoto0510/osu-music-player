namespace OsuMusicPlayer.Core.Models;

/// <summary>
/// One sample that osu! would play for a hit. <see cref="FileName"/> is set when the
/// beatmap names an explicit file; otherwise the sample is looked up by
/// <c>{SampleSetName}-{SoundName}</c> plus the custom index suffix rules.
/// </summary>
public sealed record HitsoundSample(string SampleSetName, string SoundName, int CustomIndex, double Volume, string? FileName)
{
    public string BaseName => $"{SampleSetName}-{SoundName}";

    /// <summary>The name osu! looks for inside the beatmap folder (with an index suffix from 2 upwards).</summary>
    public string BeatmapName => FileName ?? (CustomIndex >= 2 ? $"{BaseName}{CustomIndex}" : BaseName);
}

public sealed record HitsoundEvent(TimeSpan Time, IReadOnlyList<HitsoundSample> Samples);
