namespace OsuMusicPlayer.App.Services;

/// <summary>
/// Muted background-video playback that follows the audio engine. The audio engine is
/// always the clock; the video is loaded, paused, seeked and re-timed to match it.
/// </summary>
public interface IVideoPlayer : IDisposable
{
    bool IsAvailable { get; }

    /// <summary>Why video cannot be shown on this machine, or <see langword="null"/> when it can.</summary>
    string? UnavailableReason { get; }

    /// <summary>The native player object the view renders, or <see langword="null"/> when unavailable.</summary>
    object? Surface { get; }

    string? CurrentPath { get; }

    TimeSpan Position { get; }

    /// <summary>Loads a video and starts it (muted) from <paramref name="startAt"/> in video time.</summary>
    void Load(string path, TimeSpan startAt);

    void Play();

    void Pause();

    void Stop();

    void Seek(TimeSpan position);

    void SetRate(double rate);
}
