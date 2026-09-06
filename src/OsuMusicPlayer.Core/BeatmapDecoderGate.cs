namespace OsuMusicPlayer.Core;

/// <summary>
/// OsuParsers' <c>BeatmapDecoder</c> keeps the beatmap being parsed in static fields, so two
/// decodes on different threads corrupt each other. Every decode in this solution takes this
/// lock; parsing a .osu file takes a few milliseconds, so the serialisation is not noticeable.
/// </summary>
public static class BeatmapDecoderGate
{
    public static object Sync { get; } = new();
}
