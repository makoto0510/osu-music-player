using System.Collections.Concurrent;
using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.Core.Hitsounds;

/// <summary>
/// Applies osu!'s sample lookup order: the beatmap's own files first (explicit file names
/// and custom-index samples only ever come from there), then the skin, then the game's
/// default samples. Results are cached per lookup key.
/// </summary>
public sealed class HitsoundSampleResolver
{
    private readonly ISampleFileSource? beatmapSource;
    private readonly IReadOnlyList<ISampleFileSource> fallbackSources;
    private readonly ConcurrentDictionary<string, byte[]?> cache = new(StringComparer.OrdinalIgnoreCase);

    public bool PreferSkinHitsounds { get; }

    public HitsoundSampleResolver(ISampleFileSource? beatmapSource, IEnumerable<ISampleFileSource> fallbackSources, bool preferSkinHitsounds = false)
    {
        this.beatmapSource = beatmapSource;
        this.fallbackSources = fallbackSources?.ToArray() ?? throw new ArgumentNullException(nameof(fallbackSources));
        PreferSkinHitsounds = preferSkinHitsounds;
    }

    /// <summary>
    /// Identifies the audio data a sample resolves to. Index 0 and index 1 share a file name
    /// but look in different places, so the scope is part of the key.
    /// </summary>
    public static string CacheKey(HitsoundSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (sample.FileName is not null)
        {
            return "file:" + sample.FileName;
        }

        return sample.CustomIndex switch
        {
            0 => sample.BaseName + "#default",
            1 => sample.BaseName + "#beatmap",
            _ => sample.BeatmapName,
        };
    }

    public byte[]? Resolve(HitsoundSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return cache.GetOrAdd(CacheKey(sample), _ => resolveUncached(sample));
    }

    private byte[]? resolveUncached(HitsoundSample sample)
    {
        if (!PreferSkinHitsounds)
        {
            if (sample.FileName is not null)
            {
                return beatmapSource?.Read(sample.FileName);
            }

            // Custom index 1 means "the beatmap's unsuffixed samples", 2+ adds the suffix.
            if (sample.CustomIndex >= 1 && beatmapSource?.Read(sample.BeatmapName) is { } custom)
            {
                return custom;
            }
        }

        var soundToFind = sample.SoundName == "custom" ? $"{sample.SampleSetName}-hitnormal" : sample.BaseName;
        foreach (var source in fallbackSources)
        {
            if (source.Read(soundToFind) is { } bytes)
            {
                return bytes;
            }
        }

        return null;
    }
}
