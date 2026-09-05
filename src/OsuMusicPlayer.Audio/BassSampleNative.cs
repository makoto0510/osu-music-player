using ManagedBass;

namespace OsuMusicPlayer.Audio;

internal interface IBassSampleNative
{
    /// <summary>Output latency reported by the device in milliseconds, or 0 when unknown.</summary>
    int DeviceLatencyMs { get; }

    Errors LastError { get; }

    int SampleLoad(byte[] data);

    bool SampleFree(int sample);

    int SampleGetChannel(int sample);

    bool SetVolume(int channel, float volume);

    bool Play(int channel);
}

internal sealed class BassSampleNative : IBassSampleNative
{
    private const int max_simultaneous_playbacks = 16;

    public int DeviceLatencyMs
    {
        get
        {
            try
            {
                return Math.Max(0, Bass.Info.Latency);
            }
            catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
            {
                return 0;
            }
        }
    }

    public Errors LastError => Bass.LastError;

    public int SampleLoad(byte[] data) =>
        Bass.SampleLoad(data, 0, data.Length, max_simultaneous_playbacks, BassFlags.Default | BassFlags.SampleOverrideLongestPlaying);

    public bool SampleFree(int sample) => Bass.SampleFree(sample);

    public int SampleGetChannel(int sample) => Bass.SampleGetChannel(sample);

    public bool SetVolume(int channel, float volume) => Bass.ChannelSetAttribute(channel, ChannelAttribute.Volume, volume);

    public bool Play(int channel) => Bass.ChannelPlay(channel);
}
