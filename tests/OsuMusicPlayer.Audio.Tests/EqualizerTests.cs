namespace OsuMusicPlayer.Audio.Tests;

public sealed class EqualizerTests
{
    [Fact]
    public async Task Engine_AppliesGainsToCurrentAndFutureStreams()
    {
        var native = new FakeBassNative();
        using var engine = new BassAudioEngine(native, startTimer: false);
        var path = Path.GetTempFileName();
        try
        {
            engine.SetEqualizer(Equalizer.Presets["Bass Boost"]);
            engine.EqualizerGains.Should().Equal(Equalizer.Presets["Bass Boost"]);
            native.EqBands.Should().BeEmpty("there is no stream yet");

            await engine.LoadAsync(path);

            native.EqBands.Should().HaveCount(Equalizer.BandCount);
            native.EqBands[0].Gain.Should().Be(6f);
            native.EqBands[0].Center.Should().Be(31.25f);
            native.EqBands.Select(static band => band.Band).Should().Equal(Enumerable.Range(0, Equalizer.BandCount));

            engine.SetEqualizer(Equalizer.Flat);
            native.EqBands.Should().HaveCount(Equalizer.BandCount * 2, "the running stream is updated in place");
            native.EqBands[^1].Gain.Should().Be(0f);

            await engine.LoadAsync(path);
            native.EqBands.Should().HaveCount(Equalizer.BandCount * 3, "a new stream gets a fresh effect");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Normalize_ClampsAndPadsGains()
    {
        var result = Equalizer.Normalize([20f, -20f, float.NaN, 3f]);

        result.Should().HaveCount(Equalizer.BandCount);
        result[0].Should().Be(Equalizer.MaxGainDb);
        result[1].Should().Be(Equalizer.MinGainDb);
        result[2].Should().Be(0f);
        result[3].Should().Be(3f);
        result[9].Should().Be(0f);
    }

    [Fact]
    public void FindPresetName_RecognisesPresetsAndCustomCurves()
    {
        Equalizer.FindPresetName(Equalizer.Presets["Rock"]).Should().Be("Rock");
        Equalizer.FindPresetName(new float[Equalizer.BandCount]).Should().Be("Flat");
        Equalizer.FindPresetName([1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f]).Should().BeNull();
    }
}
