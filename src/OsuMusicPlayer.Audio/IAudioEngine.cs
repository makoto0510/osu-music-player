namespace OsuMusicPlayer.Audio;

public interface IAudioEngine : IDisposable
{
    event EventHandler? PlaybackEnded;

    event EventHandler<PlaybackPositionChangedEventArgs>? PositionChanged;

    AudioPlaybackState State { get; }

    TimeSpan CurrentTime { get; }

    TimeSpan TotalTime { get; }

    float Volume { get; set; }

    OsuAudioMod Mod { get; set; }

    /// <summary>Current equalizer gains in dB, one per band in <see cref="Equalizer.CenterFrequencies"/>.</summary>
    IReadOnlyList<float> EqualizerGains { get; }

    /// <summary>Applies equalizer gains to the current and every future stream.</summary>
    void SetEqualizer(IReadOnlyList<float> gainsDb);

    Task LoadAsync(string audioFilePath);

    void Play();

    void Pause();

    void Stop();

    void TogglePlay();

    void Seek(TimeSpan position);
}
