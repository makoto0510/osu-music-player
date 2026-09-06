using ManagedBass;

namespace OsuMusicPlayer.Audio;

public interface IAudioDurationProbe
{
    /// <summary>Decodes just enough of the file to learn its length, or returns <see langword="null"/> when it cannot.</summary>
    TimeSpan? Probe(string audioFilePath);
}

/// <summary>
/// Measures audio length with a BASS decode stream. Used for maps whose database entry says
/// 0 ms (work-in-progress sets that osu! never finished analysing).
/// </summary>
public sealed class BassAudioDurationProbe : IAudioDurationProbe
{
    private readonly object sync = new();

    public TimeSpan? Probe(string audioFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFilePath);
        if (!File.Exists(audioFilePath))
        {
            return null;
        }

        lock (sync)
        {
            try
            {
                var stream = Bass.CreateStream(audioFilePath, 0, 0, BassFlags.Decode | BassFlags.Prescan);
                if (stream == 0)
                {
                    return null;
                }

                try
                {
                    var bytes = Bass.ChannelGetLength(stream);
                    if (bytes < 0)
                    {
                        return null;
                    }

                    var seconds = Bass.ChannelBytes2Seconds(stream, bytes);
                    return double.IsFinite(seconds) && seconds > 0 ? TimeSpan.FromSeconds(seconds) : null;
                }
                finally
                {
                    Bass.StreamFree(stream);
                }
            }
            catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
            {
                return null; // no native BASS on this machine
            }
        }
    }
}
