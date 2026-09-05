using ManagedBass;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OsuMusicPlayer.Audio;

public sealed class BassAudioEngine : IAudioEngine
{
    private const int position_update_interval_ms = 100;

    private readonly object syncRoot = new();
    private readonly SemaphoreSlim loadGate = new(1, 1);
    private readonly IBassNative bass;
    private readonly ILogger<BassAudioEngine> logger;
    private readonly SyncProcedure endSyncProcedure;
    private readonly Timer positionTimer;

    private int stream;
    private long generation;
    private long endedGeneration = -1;
    private float volume = 1f;
    private OsuAudioMod mod;
    private bool disposed;

    public BassAudioEngine(ILogger<BassAudioEngine>? logger = null)
        : this(new BassNative(), logger, startTimer: true)
    {
    }

    internal BassAudioEngine(IBassNative bass, ILogger<BassAudioEngine>? logger = null, bool startTimer = true)
    {
        this.bass = bass ?? throw new ArgumentNullException(nameof(bass));
        this.logger = logger ?? NullLogger<BassAudioEngine>.Instance;
        endSyncProcedure = onPlaybackEnded;

        try
        {
            if (!bass.Init())
            {
                throw createBassException("BASS audio device initialization failed");
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            throw new AudioEngineException("The native BASS audio libraries could not be loaded.", exception);
        }

        positionTimer = new Timer(
            publishPosition,
            null,
            startTimer ? position_update_interval_ms : Timeout.Infinite,
            startTimer ? position_update_interval_ms : Timeout.Infinite);
    }

    public event EventHandler? PlaybackEnded;

    public event EventHandler<PlaybackPositionChangedEventArgs>? PositionChanged;

    public AudioPlaybackState State
    {
        get
        {
            lock (syncRoot)
            {
                return stream == 0 ? AudioPlaybackState.Stopped : mapState(bass.GetState(stream));
            }
        }
    }

    public TimeSpan CurrentTime
    {
        get
        {
            lock (syncRoot)
            {
                return getCurrentTimeLocked();
            }
        }
    }

    public TimeSpan TotalTime
    {
        get
        {
            lock (syncRoot)
            {
                return getTotalTimeLocked();
            }
        }
    }

    public float Volume
    {
        get
        {
            lock (syncRoot)
            {
                return volume;
            }
        }
        set
        {
            lock (syncRoot)
            {
                throwIfDisposed();
                volume = Math.Clamp(value, 0f, 1f);
                if (stream != 0 && !bass.SetAttribute(stream, ChannelAttribute.Volume, volume))
                {
                    logger.LogWarning("Could not change BASS volume: {BassError}.", bass.LastError);
                }
            }
        }
    }

    public OsuAudioMod Mod
    {
        get
        {
            lock (syncRoot)
            {
                return mod;
            }
        }
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            lock (syncRoot)
            {
                throwIfDisposed();
                mod = value;
                if (stream != 0)
                {
                    applyModLocked(stream, value);
                }
            }
        }
    }

    public async Task LoadAsync(string audioFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFilePath);
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(audioFilePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            throw new AudioEngineException("The audio file path is invalid or inaccessible.", exception);
        }

        if (!File.Exists(fullPath))
        {
            throw new AudioEngineException($"Audio file was not found: {fullPath}");
        }

        await loadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (syncRoot)
            {
                throwIfDisposed();
            }

            var newStream = await Task.Run(() =>
            {
                lock (syncRoot)
                {
                    throwIfDisposed();
                    return createTempoStream(fullPath);
                }
            }).ConfigureAwait(false);
            lock (syncRoot)
            {
                if (disposed)
                {
                    bass.FreeStream(newStream);
                    throw new ObjectDisposedException(nameof(BassAudioEngine));
                }

                releaseStreamLocked();
                stream = newStream;
                generation++;
                endedGeneration = -1;
                applyModLocked(stream, mod);
                bass.SetAttribute(stream, ChannelAttribute.Volume, volume);
                if (bass.SetEndSync(stream, endSyncProcedure, (nint)generation) == 0)
                {
                    var exception = createBassException("Could not register the BASS playback-ended callback");
                    releaseStreamLocked();
                    throw exception;
                }
            }

            publishPosition(null);
        }
        finally
        {
            loadGate.Release();
        }
    }

    public void Play()
    {
        lock (syncRoot)
        {
            throwIfDisposed();
            if (stream != 0 && !bass.Play(stream, restart: false))
            {
                logger.LogWarning("Could not start BASS playback: {BassError}.", bass.LastError);
            }
        }
    }

    public void Pause()
    {
        lock (syncRoot)
        {
            throwIfDisposed();
            if (stream != 0 && State == AudioPlaybackState.Playing && !bass.Pause(stream))
            {
                logger.LogWarning("Could not pause BASS playback: {BassError}.", bass.LastError);
            }
        }
    }

    public void Stop()
    {
        lock (syncRoot)
        {
            throwIfDisposed();
            if (stream != 0 && !bass.Stop(stream))
            {
                logger.LogWarning("Could not stop BASS playback: {BassError}.", bass.LastError);
            }
        }

        publishPosition(null);
    }

    public void TogglePlay()
    {
        if (State == AudioPlaybackState.Playing)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    public void Seek(TimeSpan position)
    {
        lock (syncRoot)
        {
            throwIfDisposed();
            if (stream == 0)
            {
                return;
            }

            var total = getTotalTimeLocked();
            var clamped = position < TimeSpan.Zero ? TimeSpan.Zero : position > total ? total : position;
            var bytePosition = bass.SecondsToBytes(stream, clamped.TotalSeconds);
            if (bytePosition < 0 || !bass.SetPosition(stream, bytePosition))
            {
                logger.LogWarning("Could not seek BASS stream: {BassError}.", bass.LastError);
            }
        }

        publishPosition(null);
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            releaseStreamLocked();
        }

        positionTimer.Dispose();
        if (!bass.Free())
        {
            logger.LogWarning("Could not release the BASS audio device: {BassError}.", bass.LastError);
        }
    }

    internal void PublishPositionForTesting() => publishPosition(null);

    private int createTempoStream(string fullPath)
    {
        var source = 0;
        try
        {
            source = bass.CreateDecodeStream(fullPath);
            if (source == 0)
            {
                throw createBassException($"Unsupported or corrupt audio file: {fullPath}");
            }

            var tempoStream = bass.CreateTempoStream(source);
            if (tempoStream != 0)
            {
                source = 0; // FxFreeSource transferred ownership to the tempo stream.
                return tempoStream;
            }

            throw createBassException("Could not create the BASS tempo stream");
        }
        catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            throw new AudioEngineException("The native BASS or BASS_FX library could not load the audio file.", exception);
        }
        finally
        {
            if (source != 0)
            {
                bass.FreeStream(source);
            }
        }
    }

    private void applyModLocked(int channel, OsuAudioMod audioMod)
    {
        var (tempo, pitch) = audioMod switch
        {
            OsuAudioMod.DT => (50f, 0f),
            OsuAudioMod.NC => (50f, ratioToSemitones(1.5)),
            OsuAudioMod.HT => (-25f, 0f),
            OsuAudioMod.DC => (-25f, ratioToSemitones(0.75)),
            _ => (0f, 0f),
        };

        if (!bass.SetAttribute(channel, ChannelAttribute.Tempo, tempo) ||
            !bass.SetAttribute(channel, ChannelAttribute.Pitch, pitch))
        {
            logger.LogWarning("Could not apply audio mod {AudioMod}: {BassError}.", audioMod, bass.LastError);
        }
    }

    private void onPlaybackEnded(int handle, int channel, int data, nint user)
    {
        bool raiseEvent;
        lock (syncRoot)
        {
            var callbackGeneration = user.ToInt64();
            raiseEvent = !disposed &&
                channel == stream &&
                callbackGeneration == generation &&
                endedGeneration != callbackGeneration;
            if (raiseEvent)
            {
                endedGeneration = callbackGeneration;
            }
        }

        if (raiseEvent)
        {
            publishPosition(null);
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void publishPosition(object? state)
    {
        PlaybackPositionChangedEventArgs? args;
        lock (syncRoot)
        {
            if (disposed || stream == 0)
            {
                return;
            }

            args = new PlaybackPositionChangedEventArgs(getCurrentTimeLocked(), getTotalTimeLocked());
        }

        PositionChanged?.Invoke(this, args);
    }

    private TimeSpan getCurrentTimeLocked()
    {
        if (stream == 0)
        {
            return TimeSpan.Zero;
        }

        var bytes = bass.GetPosition(stream);
        return bytes < 0 ? TimeSpan.Zero : safeTimeSpan(bass.BytesToSeconds(stream, bytes));
    }

    private TimeSpan getTotalTimeLocked()
    {
        if (stream == 0)
        {
            return TimeSpan.Zero;
        }

        var bytes = bass.GetLength(stream);
        return bytes < 0 ? TimeSpan.Zero : safeTimeSpan(bass.BytesToSeconds(stream, bytes));
    }

    private void releaseStreamLocked()
    {
        if (stream == 0)
        {
            return;
        }

        bass.Stop(stream);
        bass.FreeStream(stream);
        stream = 0;
    }

    private AudioEngineException createBassException(string operation) =>
        new($"{operation}. BASS error: {bass.LastError}.");

    private void throwIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private static float ratioToSemitones(double ratio) => (float)(12 * Math.Log2(ratio));

    private static TimeSpan safeTimeSpan(double seconds) =>
        double.IsFinite(seconds) && seconds > 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;

    private static AudioPlaybackState mapState(PlaybackState state) => state switch
    {
        PlaybackState.Playing or PlaybackState.Stalled => AudioPlaybackState.Playing,
        PlaybackState.Paused => AudioPlaybackState.Paused,
        _ => AudioPlaybackState.Stopped,
    };
}
