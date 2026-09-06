namespace OsuMusicPlayer.Audio;

/// <summary>The ten ISO octave bands used by the equalizer and the built-in presets.</summary>
public static class Equalizer
{
    public const int BandCount = 10;
    public const float MinGainDb = -12f;
    public const float MaxGainDb = 12f;

    public static IReadOnlyList<float> CenterFrequencies { get; } = [31.25f, 62.5f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f];

    public static IReadOnlyList<string> BandLabels { get; } = ["31", "62", "125", "250", "500", "1k", "2k", "4k", "8k", "16k"];

    public static IReadOnlyList<float> Flat { get; } = new float[BandCount];

    /// <summary>Preset name to gains in dB, in the order of <see cref="CenterFrequencies"/>.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<float>> Presets { get; } = new Dictionary<string, IReadOnlyList<float>>(StringComparer.OrdinalIgnoreCase)
    {
        ["Flat"] = Flat,
        ["Bass Boost"] = [6f, 5f, 4f, 2f, 0f, 0f, 0f, 0f, 0f, 0f],
        ["Bass Reducer"] = [-6f, -5f, -4f, -2f, 0f, 0f, 0f, 0f, 0f, 0f],
        ["Treble Boost"] = [0f, 0f, 0f, 0f, 0f, 1f, 2f, 4f, 5f, 6f],
        ["Vocal"] = [-2f, -3f, -2f, 1f, 4f, 4f, 3f, 1f, 0f, -1f],
        ["Rock"] = [5f, 4f, 3f, 1f, -1f, -1f, 1f, 3f, 4f, 5f],
        ["Pop"] = [-1f, 0f, 2f, 4f, 4f, 2f, 0f, -1f, -1f, -1f],
        ["Electronic"] = [5f, 4f, 1f, 0f, -2f, 1f, 1f, 2f, 4f, 5f],
        ["Acoustic"] = [4f, 3f, 2f, 1f, 2f, 2f, 3f, 3f, 2f, 1f],
        ["Loudness"] = [6f, 4f, 0f, 0f, -2f, 0f, -1f, 0f, 4f, 2f],
    };

    public static IReadOnlyList<string> PresetNames { get; } = Presets.Keys.ToArray();

    /// <summary>Returns the preset whose gains equal <paramref name="gains"/>, or <see langword="null"/> for a custom curve.</summary>
    public static string? FindPresetName(IReadOnlyList<float> gains)
    {
        ArgumentNullException.ThrowIfNull(gains);
        foreach (var (name, presetGains) in Presets)
        {
            if (presetGains.Count == gains.Count && presetGains.Zip(gains).All(static pair => Math.Abs(pair.First - pair.Second) < 0.01f))
            {
                return name;
            }
        }

        return null;
    }

    public static float[] Normalize(IReadOnlyList<float>? gains)
    {
        var result = new float[BandCount];
        if (gains is null)
        {
            return result;
        }

        for (var i = 0; i < Math.Min(BandCount, gains.Count); i++)
        {
            result[i] = float.IsFinite(gains[i]) ? Math.Clamp(gains[i], MinGainDb, MaxGainDb) : 0f;
        }

        return result;
    }
}
