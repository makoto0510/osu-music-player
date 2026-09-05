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

    Task LoadAsync(string audioFilePath);

    void Play();

    void Pause();

    void Stop();

    void TogglePlay();

    void Seek(TimeSpan position);
}
