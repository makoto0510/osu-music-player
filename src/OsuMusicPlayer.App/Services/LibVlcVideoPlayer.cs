using System.Globalization;
using LibVLCSharp.Shared;

namespace OsuMusicPlayer.App.Services;

/// <summary>Background-video playback through libVLC. Audio output is disabled entirely.</summary>
public sealed class LibVlcVideoPlayer : IVideoPlayer
{
    private readonly object syncRoot = new();
    private readonly LibVLC libVlc;
    private readonly MediaPlayer player;
    private Media? media;
    private TimeSpan? pendingSeek;
    private float requestedRate = 1;
    private bool shouldPlay;
    private bool disposed;
    private int mediaGeneration;

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
        player.Playing += onPlaying;
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

        Media? previous;
        Media next;
        lock (syncRoot)
        {
            var clamped = startAt < TimeSpan.Zero ? TimeSpan.Zero : startAt;
            next = new Media(libVlc, new Uri(Path.GetFullPath(path)));
            if (clamped > TimeSpan.Zero)
            {
                // The media option gets decoding close to the requested keyframe. The pending
                // seek below makes the position exact once libVLC reports the player as ready.
                next.AddOption(":start-time=" + clamped.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
                pendingSeek = clamped;
            }
            else
            {
                pendingSeek = null;
            }

            previous = media;
            media = next;
            CurrentPath = path;
            shouldPlay = true;
            mediaGeneration++;
        }

        // LibVLC can synchronously wait for its event thread from Play/Stop. Never call it
        // while holding syncRoot because the Playing callback also reads the desired state.
        if (previous is not null)
        {
            player.Stop();
            previous.Dispose();
        }
        player.Play(next);
    }

    public void Play()
    {
        throwIfDisposed();
        Media? current;
        VLCState state;
        lock (syncRoot)
        {
            shouldPlay = true;
            current = media;
        }

        if (current is null)
        {
            return;
        }

        state = player.State;
        if (state is VLCState.Ended or VLCState.Stopped or VLCState.Error)
        {
            player.Play(current);
        }
        else
        {
            player.SetPause(false);
        }
    }

    public void Pause()
    {
        throwIfDisposed();
        lock (syncRoot)
        {
            shouldPlay = false;
        }

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
        var applyNow = false;
        var clamped = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        lock (syncRoot)
        {
            if (media is null)
            {
                return;
            }

            pendingSeek = clamped;
            applyNow = player.IsSeekable;
        }

        if (applyNow)
        {
            player.Time = (long)clamped.TotalMilliseconds;
            clearPendingSeek(clamped);
        }
    }

    public void SetRate(double rate)
    {
        throwIfDisposed();
        if (double.IsFinite(rate) && rate > 0)
        {
            var hasMedia = false;
            lock (syncRoot)
            {
                requestedRate = (float)rate;
                hasMedia = media is not null;
            }

            if (hasMedia)
            {
                player.SetRate((float)rate);
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        lock (syncRoot)
        {
            disposed = true;
            mediaGeneration++;
        }
        player.Playing -= onPlaying;
        releaseMedia();
        player.Dispose();
        libVlc.Dispose();
    }

    private void releaseMedia()
    {
        Media? previous;
        lock (syncRoot)
        {
            shouldPlay = false;
            pendingSeek = null;
            previous = media;
            media = null;
            CurrentPath = null;
            mediaGeneration++;
        }

        if (previous is not null)
        {
            player.Stop();
            previous.Dispose();
        }
    }

    private void onPlaying(object? sender, EventArgs args)
    {
        int generation;
        lock (syncRoot)
        {
            if (disposed || media is null)
            {
                return;
            }

            generation = mediaGeneration;
        }

        // Returning from the native callback before controlling the player avoids re-entering
        // libVLC while it is still completing Play().
        ThreadPool.QueueUserWorkItem(static state =>
        {
            var (owner, expectedGeneration) = ((LibVlcVideoPlayer, int))state!;
            owner.applyReadyState(expectedGeneration);
        }, (this, generation));
    }

    private void applyReadyState(int expectedGeneration)
    {
        float rate;
        TimeSpan? seek;
        bool play;
        lock (syncRoot)
        {
            if (disposed || media is null || mediaGeneration != expectedGeneration)
            {
                return;
            }

            rate = requestedRate;
            seek = pendingSeek;
            play = shouldPlay;
        }

        try
        {
            player.SetRate(rate);
            if (seek is { } position && player.IsSeekable)
            {
                player.Time = (long)position.TotalMilliseconds;
                clearPendingSeek(position);
            }

            if (!play && player.CanPause)
            {
                player.SetPause(true);
            }
        }
        catch (ObjectDisposedException)
        {
            // Shutdown raced with the queued ready-state application.
        }
    }

    private void clearPendingSeek(TimeSpan applied)
    {
        lock (syncRoot)
        {
            if (pendingSeek == applied)
            {
                pendingSeek = null;
            }
        }
    }

    private void throwIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
