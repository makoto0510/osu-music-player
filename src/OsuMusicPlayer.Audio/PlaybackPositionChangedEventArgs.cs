namespace OsuMusicPlayer.Audio;

public sealed class PlaybackPositionChangedEventArgs(TimeSpan currentTime, TimeSpan totalTime) : EventArgs
{
    public TimeSpan CurrentTime { get; } = currentTime;

    public TimeSpan TotalTime { get; } = totalTime;
}
