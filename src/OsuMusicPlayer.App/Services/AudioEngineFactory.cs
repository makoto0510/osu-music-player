using OsuMusicPlayer.Audio;

namespace OsuMusicPlayer.App.Services;

internal static class AudioEngineFactory
{
    public static IAudioEngine Create()
    {
        try
        {
            return new BassAudioEngine();
        }
        catch (AudioEngineException exception)
        {
            return new UnavailableAudioEngine(exception);
        }
    }
}
