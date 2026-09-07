using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OsuMusicPlayer.Core.Hitsounds;
using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.Audio;

/// <summary>
/// BASS-backed hit sound playback. A worker thread polls the audio engine's clock a few
/// hundred times per second and triggers pre-loaded samples slightly ahead of time to
/// cancel out the output device latency.
/// </summary>
public sealed class BassHitsoundPlayer : IHitsoundPlayer
{
    private static readonly TimeSpan max_lateness = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan idle_poll = TimeSpan.FromMilliseconds(25);

    private readonly object sync = new();
    private readonly IBassSampleNative native;
    private readonly IAudioEngine engine;
    private readonly ILogger<BassHitsoundPlayer> logger;
    private readonly ManualResetEventSlim wake = new(false);
    private readonly Thread worker;
    private Dictionary<string, int> samples = new(StringComparer.OrdinalIgnoreCase);
    private HitsoundScheduler? scheduler;
    private volatile bool enabled;
    private volatile bool disposed;
    private float volume = 1f;
    private int offsetMs;
    private int loadVersion;

    public BassHitsoundPlayer(IAudioEngine engine, ILogger<BassHitsoundPlayer>? logger = null)
        : this(new BassSampleNative(), engine, logger)
    {
    }

    internal BassHitsoundPlayer(IBassSampleNative native, IAudioEngine engine, ILogger<BassHitsoundPlayer>? logger = null, bool startWorker = true)
    {
        this.native = native ?? throw new ArgumentNullException(nameof(native));
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.logger = logger ?? NullLogger<BassHitsoundPlayer>.Instance;
        worker = new Thread(run)
        {
            IsBackground = true,
            Name = "osu! hitsounds",
            Priority = ThreadPriority.AboveNormal,
        };
        if (startWorker)
        {
            worker.Start();
        }
    }

    public event EventHandler<HitsoundEvent>? HitPlayed;

    public bool IsEnabled
    {
        get => enabled;
        set
        {
            enabled = value;
            wake.Set();
        }
    }

    public float Volume
    {
        get => volume;
        set => volume = Math.Clamp(value, 0f, 1f);
    }

    public int OffsetMs
    {
        get => offsetMs;
        set => offsetMs = Math.Clamp(value, -500, 500);
    }

    public int MissingSampleCount { get; private set; }

    public async Task LoadAsync(IReadOnlyList<HitsoundEvent> events, HitsoundSampleResolver resolver, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(resolver);
        ObjectDisposedException.ThrowIf(disposed, this);

        var version = Interlocked.Increment(ref loadVersion);
        var loaded = await Task.Run(() =>
        {
            var handles = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var missing = 0;
            foreach (var sample in events.SelectMany(static hit => hit.Samples))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = HitsoundSampleResolver.CacheKey(sample);
                if (handles.ContainsKey(key))
                {
                    continue;
                }

                var data = resolver.Resolve(sample);
                if (data is null || data.Length == 0)
                {
                    missing++;
                    handles[key] = 0;
                    continue;
                }

                int handle;
                try
                {
                    handle = native.SampleLoad(data);
                }
                catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
                {
                    handle = 0; // no native BASS on this machine: hit sounds stay silent
                }

                if (handle == 0)
                {
                    logger.LogDebug("BASS could not decode hit sound {Sample}: {Error}.", key, native.LastError);
                    missing++;
                }

                handles[key] = handle;
            }

            return (Handles: handles, Missing: missing);
        }, cancellationToken).ConfigureAwait(false);

        Dictionary<string, int> previous;
        lock (sync)
        {
            if (disposed || version != loadVersion)
            {
                freeAll(loaded.Handles);
                return;
            }

            previous = samples;
            samples = loaded.Handles;
            scheduler = new HitsoundScheduler(events.OrderBy(static hit => hit.Time).ToArray(), max_lateness);
            MissingSampleCount = loaded.Missing;
        }

        freeAll(previous);
        wake.Set();
    }

    public void Clear()
    {
        Interlocked.Increment(ref loadVersion);
        Dictionary<string, int> previous;
        lock (sync)
        {
            previous = samples;
            samples = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            scheduler = null;
            MissingSampleCount = 0;
        }

        freeAll(previous);
    }

    /// <summary>Runs one scheduling step; exposed for tests that do not start the worker thread.</summary>
    internal int TickForTesting(TimeSpan now) => tick(now);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        wake.Set();
        Clear();
        if (worker.IsAlive && Thread.CurrentThread != worker)
        {
            worker.Join(TimeSpan.FromSeconds(1));
        }

        wake.Dispose();
    }

    private void run()
    {
        var latency = TimeSpan.FromMilliseconds(native.DeviceLatencyMs);
        while (!disposed)
        {
            try
            {
                HitsoundScheduler? current;
                lock (sync)
                {
                    current = scheduler;
                }

                if (!enabled || current is null || engine.State != AudioPlaybackState.Playing)
                {
                    wake.Wait(idle_poll);
                    wake.Reset();
                    continue;
                }

                // Fire early by the device latency so the sample is heard on the beat.
                var now = engine.CurrentTime + latency + TimeSpan.FromMilliseconds(offsetMs);
                tick(now);

                var next = current.NextDue;
                if (next is null)
                {
                    wake.Wait(idle_poll);
                    wake.Reset();
                    continue;
                }

                var delay = next.Value - now;
                if (delay > TimeSpan.FromMilliseconds(4))
                {
                    Thread.Sleep(TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds - 2, 8)));
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
            catch (Exception exception) when (exception is ObjectDisposedException)
            {
                return;
            }
        }
    }

    private int tick(TimeSpan now)
    {
        IReadOnlyList<HitsoundEvent> due;
        Dictionary<string, int> currentSamples;
        lock (sync)
        {
            if (scheduler is null)
            {
                return 0;
            }

            due = scheduler.Advance(now);
            currentSamples = samples;
        }

        var played = 0;
        var masterVolume = volume * engine.Volume;
        foreach (var hit in due)
        {
            HitPlayed?.Invoke(this, hit);
            foreach (var sample in hit.Samples)
            {
                if (!currentSamples.TryGetValue(HitsoundSampleResolver.CacheKey(sample), out var handle) || handle == 0)
                {
                    continue;
                }

                var channel = native.SampleGetChannel(handle);
                if (channel == 0)
                {
                    continue;
                }

                native.SetVolume(channel, (float)Math.Clamp(sample.Volume * masterVolume, 0, 1));
                if (native.Play(channel))
                {
                    played++;
                }
            }
        }

        return played;
    }

    private void freeAll(Dictionary<string, int> handles)
    {
        foreach (var handle in handles.Values.Where(static handle => handle != 0).Distinct())
        {
            native.SampleFree(handle);
        }
    }
}
