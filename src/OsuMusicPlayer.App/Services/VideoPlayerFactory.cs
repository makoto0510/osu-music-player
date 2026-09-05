using LibVLCSharp.Shared;

namespace OsuMusicPlayer.App.Services;

internal static class VideoPlayerFactory
{
    public static IVideoPlayer Create()
    {
        try
        {
            return new LibVlcVideoPlayer();
        }
        catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or TypeInitializationException or VLCException or InvalidOperationException)
        {
            return new UnavailableVideoPlayer($"libVLC could not be loaded: {exception.Message}");
        }
    }
}

/// <summary>Stand-in used when libVLC is missing, e.g. on macOS/Linux without the native package.</summary>
internal sealed class UnavailableVideoPlayer(string reason) : IVideoPlayer
{
    public bool IsAvailable => false;
    public string? UnavailableReason { get; } = reason;
    public object? Surface => null;
    public string? CurrentPath => null;
    public TimeSpan Position => TimeSpan.Zero;
    public void Load(string path, TimeSpan startAt) { }
    public void Play() { }
    public void Pause() { }
    public void Stop() { }
    public void Seek(TimeSpan position) { }
    public void SetRate(double rate) { }
    public void Dispose() { }
}
