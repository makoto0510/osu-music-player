using OsuMusicPlayer.Core.Hitsounds;
using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.Audio.Tests;

public sealed class HitsoundPlayerTests
{
    private static readonly HitsoundSample normal = new("soft", "hitnormal", 1, 0.5, null);
    private static readonly HitsoundSample clap = new("soft", "hitclap", 1, 1, null);

    [Fact]
    public void Scheduler_ReturnsDueEventsOnceAndSkipsStaleOnesAfterForwardSeek()
    {
        var scheduler = new HitsoundScheduler(
        [
            new HitsoundEvent(TimeSpan.FromMilliseconds(100), [normal]),
            new HitsoundEvent(TimeSpan.FromMilliseconds(200), [normal]),
            new HitsoundEvent(TimeSpan.FromMilliseconds(5000), [normal]),
            new HitsoundEvent(TimeSpan.FromMilliseconds(5100), [normal]),
        ], TimeSpan.FromMilliseconds(150));

        scheduler.Advance(TimeSpan.FromMilliseconds(50)).Should().BeEmpty();
        scheduler.Advance(TimeSpan.FromMilliseconds(210)).Select(static hit => hit.Time.TotalMilliseconds).Should().Equal(100, 200);
        scheduler.Advance(TimeSpan.FromMilliseconds(220)).Should().BeEmpty("events fire only once");
        scheduler.NextDue.Should().Be(TimeSpan.FromMilliseconds(5000));

        // A seek far ahead drops the event that is now long overdue but keeps the fresh one.
        scheduler.Advance(TimeSpan.FromMilliseconds(5200)).Select(static hit => hit.Time.TotalMilliseconds).Should().Equal(5100);
    }

    [Fact]
    public void Scheduler_RewindsAfterBackwardSeek()
    {
        var scheduler = new HitsoundScheduler(
        [
            new HitsoundEvent(TimeSpan.FromMilliseconds(100), [normal]),
            new HitsoundEvent(TimeSpan.FromMilliseconds(200), [normal]),
        ], TimeSpan.FromMilliseconds(150));

        scheduler.Advance(TimeSpan.FromMilliseconds(250)).Should().HaveCount(2);
        scheduler.Advance(TimeSpan.FromMilliseconds(90)).Should().BeEmpty();
        scheduler.Advance(TimeSpan.FromMilliseconds(120)).Select(static hit => hit.Time.TotalMilliseconds).Should().Equal(100);
    }

    [Fact]
    public async Task Player_LoadsDistinctSamplesPlaysDueOnesAndFreesOnClear()
    {
        var native = new FakeSampleNative();
        var engine = new FakeEngine { Volume = 0.2f };
        using var player = new BassHitsoundPlayer(native, engine, startWorker: false) { IsEnabled = true, Volume = 0.5f };
        var resolver = new HitsoundSampleResolver(new FakeSource(("soft-hitnormal.wav", [1, 2]), ("soft-hitclap.wav", [3])), []);

        await player.LoadAsync(
        [
            new HitsoundEvent(TimeSpan.FromMilliseconds(100), [normal, clap]),
            new HitsoundEvent(TimeSpan.FromMilliseconds(300), [normal]),
            new HitsoundEvent(TimeSpan.FromMilliseconds(400), [new HitsoundSample("drum", "hitfinish", 1, 1, null)]),
        ], resolver);

        native.LoadedCount.Should().Be(2, "identical samples are decoded once");
        player.MissingSampleCount.Should().Be(1, "drum-hitfinish is not available anywhere");

        player.TickForTesting(TimeSpan.FromMilliseconds(120)).Should().Be(2);
        native.Played.Should().HaveCount(2);
        native.Played[0].Volume.Should().BeApproximately(0.25f, 0.001f, "sample volume is scaled by the independent effect volume");
        native.Played[1].Volume.Should().BeApproximately(0.5f, 0.001f);

        player.TickForTesting(TimeSpan.FromMilliseconds(450)).Should().Be(1, "the missing sample is skipped silently");

        player.Clear();
        native.FreedCount.Should().Be(2);
        player.TickForTesting(TimeSpan.FromMilliseconds(999)).Should().Be(0);
    }

    [Fact]
    public async Task Player_ReplacingTimelineFreesOldSamples()
    {
        var native = new FakeSampleNative();
        using var player = new BassHitsoundPlayer(native, new FakeEngine(), startWorker: false);
        var resolver = new HitsoundSampleResolver(new FakeSource(("soft-hitnormal.wav", [1]), ("soft-hitclap.wav", [3])), []);

        await player.LoadAsync([new HitsoundEvent(TimeSpan.Zero, [normal])], resolver);
        await player.LoadAsync([new HitsoundEvent(TimeSpan.Zero, [clap])], resolver);

        native.LoadedCount.Should().Be(2);
        native.FreedCount.Should().Be(1);
    }

    [Theory]
    [InlineData(OsuAudioMod.NC)]
    [InlineData(OsuAudioMod.DC)]
    [InlineData(OsuAudioMod.DT)]
    [InlineData(OsuAudioMod.HT)]
    [InlineData(OsuAudioMod.None)]
    public async Task Player_DoesNotPitchSamplesOnMods(OsuAudioMod mod)
    {
        var native = new FakeSampleNative();
        var engine = new FakeEngine { Mod = mod };
        using var player = new BassHitsoundPlayer(native, engine, startWorker: false) { IsEnabled = true };
        var resolver = new HitsoundSampleResolver(new FakeSource(("soft-hitnormal.wav", [1])), []);

        await player.LoadAsync([new HitsoundEvent(TimeSpan.FromMilliseconds(100), [normal])], resolver);
        player.TickForTesting(TimeSpan.FromMilliseconds(120));

        native.Played.Should().HaveCount(1);
        native.Ratios.Should().BeEmpty("hitsound samples must not change pitch even when NC/DC mods are active");
    }

    private sealed class FakeSampleNative : IBassSampleNative
    {
        private int nextHandle = 100;

        public int DeviceLatencyMs => 0;
        public ManagedBass.Errors LastError => ManagedBass.Errors.OK;
        public List<(int Channel, float Ratio)> Ratios { get; } = [];
        public bool SetFrequencyRatio(int channel, float ratio) { Ratios.Add((channel, ratio)); return true; }
        public int LoadedCount { get; private set; }
        public int FreedCount { get; private set; }
        public List<(int Channel, float Volume)> Played { get; } = [];
        private readonly Dictionary<int, float> volumes = [];

        public int SampleLoad(byte[] data) { LoadedCount++; return nextHandle++; }
        public bool SampleFree(int sample) { FreedCount++; return true; }
        public int SampleGetChannel(int sample) => sample * 10;
        public bool SetVolume(int channel, float volume) { volumes[channel] = volume; return true; }
        public bool Play(int channel) { Played.Add((channel, volumes.GetValueOrDefault(channel, 1f))); return true; }
    }

    private sealed class FakeEngine : IAudioEngine
    {
        public event EventHandler? PlaybackEnded { add { } remove { } }
        public event EventHandler<PlaybackPositionChangedEventArgs>? PositionChanged { add { } remove { } }
        public AudioPlaybackState State { get; set; } = AudioPlaybackState.Playing;
        public TimeSpan CurrentTime { get; set; }
        public TimeSpan TotalTime => TimeSpan.FromMinutes(2);
        public float Volume { get; set; } = 1;
        public OsuAudioMod Mod { get; set; }
        public IReadOnlyList<float> EqualizerGains { get; private set; } = new float[Equalizer.BandCount];
        public void SetEqualizer(IReadOnlyList<float> gainsDb) => EqualizerGains = Equalizer.Normalize(gainsDb);
        public Task LoadAsync(string audioFilePath) => Task.CompletedTask;
        public void Play() { }
        public void Pause() { }
        public void Stop() { }
        public void TogglePlay() { }
        public void Seek(TimeSpan position) { }
        public void Dispose() { }
    }

    private sealed class FakeSource(params (string Name, byte[] Data)[] files) : ISampleFileSource
    {
        public byte[]? Read(string name)
        {
            foreach (var candidate in Path.HasExtension(name) ? [name] : new[] { name + ".wav", name + ".ogg", name + ".mp3" })
            {
                var match = files.FirstOrDefault(file => string.Equals(file.Name, candidate, StringComparison.OrdinalIgnoreCase));
                if (match.Data is not null)
                {
                    return match.Data;
                }
            }

            return null;
        }
    }
}
