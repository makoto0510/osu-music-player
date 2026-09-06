using OsuMusicPlayer.Audio;

namespace OsuMusicPlayer.App.Services;

internal sealed class UnavailableAudioEngine(AudioEngineException error) : IAudioEngine
{
    private float volume = 1f;
    private OsuAudioMod mod;

    public event EventHandler? PlaybackEnded { add { } remove { } }
    public event EventHandler<PlaybackPositionChangedEventArgs>? PositionChanged { add { } remove { } }
    public AudioPlaybackState State => AudioPlaybackState.Stopped;
    public TimeSpan CurrentTime => TimeSpan.Zero;
    public TimeSpan TotalTime => TimeSpan.Zero;
    public float Volume { get => volume; set => volume = Math.Clamp(value, 0f, 1f); }
    public OsuAudioMod Mod { get => mod; set => mod = value; }
    public IReadOnlyList<float> EqualizerGains { get; private set; } = new float[Equalizer.BandCount];
    public void SetEqualizer(IReadOnlyList<float> gainsDb) => EqualizerGains = Equalizer.Normalize(gainsDb);
    public Task LoadAsync(string audioFilePath) => Task.FromException(new AudioEngineException(error.Message, error));
    public void Play() { }
    public void Pause() { }
    public void Stop() { }
    public void TogglePlay() { }
    public void Seek(TimeSpan position) { }
    public void Dispose() { }
}
