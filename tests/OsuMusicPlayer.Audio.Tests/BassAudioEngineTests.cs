using ManagedBass;
using OsuMusicPlayer.Audio;

namespace OsuMusicPlayer.Audio.Tests;

public sealed class BassAudioEngineTests
{
    [Fact]
    public void ConstructorAndDispose_OwnBassLifetime()
    {
        var bass = new FakeBassNative();
        var engine = new BassAudioEngine(bass, startTimer: false);

        engine.Dispose();
        engine.Dispose();

        bass.InitCalled.Should().BeTrue();
        bass.FreeCalled.Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_ReplacesAndReleasesPreviousStream()
    {
        var bass = new FakeBassNative();
        using var engine = new BassAudioEngine(bass, startTimer: false);
        var path = createAudioPlaceholder();
        try
        {
            await engine.LoadAsync(path);
            var first = bass.CurrentTempoStream;
            await engine.LoadAsync(path);

            bass.FreedStreams.Should().Contain(first);
            engine.TotalTime.Should().Be(TimeSpan.FromSeconds(120));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(OsuAudioMod.None, 0, 0)]
    [InlineData(OsuAudioMod.DT, 50, 0)]
    [InlineData(OsuAudioMod.NC, 50, 7.01955)]
    [InlineData(OsuAudioMod.HT, -25, 0)]
    [InlineData(OsuAudioMod.DC, -25, -4.98045)]
    public async Task Mod_AppliesTempoAndPitch(OsuAudioMod mod, float tempo, float pitch)
    {
        var bass = new FakeBassNative();
        using var engine = new BassAudioEngine(bass, startTimer: false);
        var path = createAudioPlaceholder();
        try
        {
            await engine.LoadAsync(path);
            bass.Attributes.Clear();
            engine.Mod = mod;

            bass.Attributes.Should().Contain(entry => entry.Attribute == ChannelAttribute.Tempo && Math.Abs(entry.Value - tempo) < 0.001);
            bass.Attributes.Should().Contain(entry => entry.Attribute == ChannelAttribute.Pitch && Math.Abs(entry.Value - pitch) < 0.001);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task VolumeSeekAndEnd_AreClampedAndPublished()
    {
        var bass = new FakeBassNative();
        using var engine = new BassAudioEngine(bass, startTimer: false);
        var path = createAudioPlaceholder();
        try
        {
            await engine.LoadAsync(path);
            engine.Volume = 2;
            engine.Seek(TimeSpan.FromMinutes(10));
            var ended = 0;
            engine.PlaybackEnded += (_, _) => ended++;
            bass.RaiseEnded();
            bass.RaiseEnded();

            engine.Volume.Should().Be(1);
            bass.Position.Should().Be(120_000);
            ended.Should().Be(1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RepeatTrack_RaisesPlaybackEndedEachTimeTrackReachesEnd()
    {
        var bass = new FakeBassNative();
        using var engine = new BassAudioEngine(bass, startTimer: false);
        var path = createAudioPlaceholder();
        try
        {
            await engine.LoadAsync(path);
            var ended = 0;
            engine.PlaybackEnded += (_, _) => ended++;

            // First track end
            bass.RaiseEnded();
            bass.RaiseEnded(); // Duplicate should be debounced
            ended.Should().Be(1);

            // Repeat: rewind and play again
            engine.Seek(TimeSpan.Zero);
            engine.Play();

            // Second track end
            bass.RaiseEnded();
            bass.RaiseEnded(); // Duplicate should be debounced
            ended.Should().Be(2);

            // Third repeat
            engine.Seek(TimeSpan.Zero);
            engine.Play();
            bass.RaiseEnded();
            ended.Should().Be(3);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PlaybackControlsAndPositionEvent_ReflectNativeState()
    {
        var bass = new FakeBassNative();
        using var engine = new BassAudioEngine(bass, startTimer: false);
        var path = createAudioPlaceholder();
        try
        {
            await engine.LoadAsync(path);
            engine.Play();
            engine.State.Should().Be(AudioPlaybackState.Playing);
            engine.TogglePlay();
            engine.State.Should().Be(AudioPlaybackState.Paused);
            engine.TogglePlay();
            engine.Stop();
            engine.State.Should().Be(AudioPlaybackState.Stopped);

            PlaybackPositionChangedEventArgs? received = null;
            engine.PositionChanged += (_, args) => received = args;
            bass.Position = 42_000;
            engine.PublishPositionForTesting();

            received.Should().NotBeNull();
            received?.CurrentTime.Should().Be(TimeSpan.FromSeconds(42));
            received?.TotalTime.Should().Be(TimeSpan.FromSeconds(120));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TempoCreationFailure_ReleasesDecodeStream()
    {
        var bass = new FakeBassNative { FailTempo = true };
        using var engine = new BassAudioEngine(bass, startTimer: false);
        var path = createAudioPlaceholder();
        try
        {
            await FluentActions.Awaiting(() => engine.LoadAsync(path)).Should().ThrowAsync<AudioEngineException>();
            bass.FreedStreams.Should().Contain(bass.LastDecodeStream);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task InvalidAudioAndSyncFailure_DoNotLeakStreams()
    {
        var bass = new FakeBassNative { FailDecode = true };
        using var engine = new BassAudioEngine(bass, startTimer: false);
        var path = createAudioPlaceholder();
        try
        {
            await FluentActions.Awaiting(() => engine.LoadAsync(path)).Should().ThrowAsync<AudioEngineException>();
            bass.FailDecode = false;
            bass.FailSync = true;
            await FluentActions.Awaiting(() => engine.LoadAsync(path)).Should().ThrowAsync<AudioEngineException>();
            bass.FreedStreams.Should().Contain(bass.CurrentTempoStream);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string createAudioPlaceholder()
    {
        var path = Path.Combine(Path.GetTempPath(), $"osu-player-{Guid.NewGuid():N}.audio");
        File.WriteAllBytes(path, [0]);
        return path;
    }
}
