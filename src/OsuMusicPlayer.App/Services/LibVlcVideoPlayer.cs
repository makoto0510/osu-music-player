using System.Globalization;
using LibVLCSharp.Shared;

namespace OsuMusicPlayer.App.Services;

/// <summary>Background-video playback through libVLC. Audio output is disabled entirely.</summary>
public sealed class LibVlcVideoPlayer : IVideoPlayer
{
    private readonly LibVLC libVlc;
    private readonly MediaPlayer player;
    private Media? media;
    private bool disposed;

    public LibVlcVideoPlayer()
    {
        LibVLCSharp.Shared.Core.Initialize();
        libVlc = new LibVLC("--no-audio", "--no-video-title-show", "--no-osd", "--quiet", "--no-spu");
        player = new MediaPlayer(libVlc)
        {
            EnableHardwareDecoding = true,
            Mute = true,
            Volume = 0,
        };
    }

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public object? Surface => player;

    public string? CurrentPath { get; private set; }

    public TimeSpan Position => player.Time < 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(player.Time);

    public void Load(string path, TimeSpan startAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        throwIfDisposed();

        releaseMedia();
        media = new Media(libVlc, new Uri(Path.GetFullPath(path)));
        if (startAt > TimeSpan.Zero)
        {
            media.AddOption(":start-time=" + startAt.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        }

        CurrentPath = path;
        player.Play(media);
    }

    public void Play()
    {
        throwIfDisposed();
        if (media is null)
        {
            return;
        }

        if (player.State is VLCState.Ended or VLCState.Stopped or VLCState.Error)
        {
            player.Play(media);
        }
        else
        {
            player.SetPause(false);
        }
    }

    public void Pause()
    {
        throwIfDisposed();
        if (player.CanPause)
        {
            player.SetPause(true);
        }
    }

    public void Stop()
    {
        throwIfDisposed();
        releaseMedia();
    }

    public void Seek(TimeSpan position)
    {
        throwIfDisposed();
        if (media is null)
        {
            return;
        }

        var clamped = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        if (player.IsSeekable)
        {
            player.Time = (long)clamped.TotalMilliseconds;
        }
    }

    public void SetRate(double rate)
    {
        throwIfDisposed();
        if (double.IsFinite(rate) && rate > 0)
        {
            player.SetRate((float)rate);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        releaseMedia();
        player.Dispose();
        libVlc.Dispose();
    }

    private void releaseMedia()
    {
        if (media is null)
        {
            CurrentPath = null;
            return;
        }

        player.Stop();
        media.Dispose();
        media = null;
        CurrentPath = null;
    }

    private void throwIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
