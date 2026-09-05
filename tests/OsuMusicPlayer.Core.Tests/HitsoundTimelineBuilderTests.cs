using FluentAssertions;
using OsuMusicPlayer.Core.Hitsounds;
using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.Core.Tests;

public sealed class HitsoundTimelineBuilderTests
{
    private const string header = """
        osu file format v14

        [General]
        AudioFilename: audio.mp3
        SampleSet: Normal
        Mode: 0

        [Difficulty]
        HPDrainRate:5
        CircleSize:4
        OverallDifficulty:7
        ApproachRate:9
        SliderMultiplier:1
        SliderTickRate:1

        [TimingPoints]
        0,500,4,2,1,60,1,0
        2000,-100,4,3,2,80,0,0

        """;

    [Fact]
    public void Circle_UsesTimingPointSetIndexAndVolumeAndLayersAdditions()
    {
        var events = HitsoundTimelineBuilder.Build(lines(header + """
            [HitObjects]
            100,100,1000,1,10,0:0:0:0:
            """));

        var hit = events.Should().ContainSingle().Which;
        hit.Time.Should().Be(TimeSpan.FromMilliseconds(1000));
        hit.Samples.Select(static sample => sample.BaseName).Should().Equal("soft-hitnormal", "soft-hitwhistle", "soft-hitclap");
        hit.Samples.Should().AllSatisfy(sample =>
        {
            sample.CustomIndex.Should().Be(1);
            sample.Volume.Should().BeApproximately(0.6, 0.0001);
        });
    }

    [Fact]
    public void Circle_ExtrasOverrideSetsIndexAndVolume_AndInheritedPointsCount()
    {
        var events = HitsoundTimelineBuilder.Build(lines(header + """
            [HitObjects]
            100,100,2500,1,4,0:0:0:0:
            100,100,3000,1,2,1:3:5:30:
            """));

        events.Should().HaveCount(2);
        events[0].Samples.Select(static sample => sample.BaseName).Should().Equal("drum-hitnormal", "drum-hitfinish");
        events[0].Samples[0].CustomIndex.Should().Be(2, "the inherited timing point at 2000 sets custom sample set 2");
        events[0].Samples[0].Volume.Should().BeApproximately(0.8, 0.0001);

        events[1].Samples.Select(static sample => sample.BaseName).Should().Equal("normal-hitnormal", "drum-hitwhistle");
        events[1].Samples[0].CustomIndex.Should().Be(5);
        events[1].Samples[0].Volume.Should().BeApproximately(0.3, 0.0001);
        events[1].Samples[0].BeatmapName.Should().Be("normal-hitnormal5");
    }

    [Fact]
    public void Circle_WithExplicitFile_PlaysOnlyThatFile()
    {
        var events = HitsoundTimelineBuilder.Build(lines(header + """
            [HitObjects]
            100,100,1000,1,14,0:0:0:70:C#5_s.wav
            """));

        var sample = events.Should().ContainSingle().Which.Samples.Should().ContainSingle().Which;
        sample.FileName.Should().Be("C#5_s.wav");
        sample.BeatmapName.Should().Be("C#5_s.wav");
        sample.Volume.Should().BeApproximately(0.7, 0.0001);
    }

    [Fact]
    public void Slider_ProducesHeadRepeatsEndAndTicksWithEdgeSounds()
    {
        // 1 beat = 500 ms, SV 1 => 100 px per beat; length 200 px = 2 beats = 1000 ms per span.
        var events = HitsoundTimelineBuilder.Build(lines(header + """
            [HitObjects]
            100,100,1000,2,0,L|300:100,2,200,2|0|8,0:0|3:0|0:2,0:0:0:0:
            """));

        var hits = events.Where(static hit => hit.Samples.Any(static sample => sample.SoundName != "slidertick")).ToArray();
        hits.Select(static hit => hit.Time.TotalMilliseconds).Should().Equal(1000, 2000, 3000);
        hits[0].Samples.Select(static sample => sample.BaseName).Should().Equal("soft-hitnormal", "soft-hitwhistle");
        hits[1].Samples.Select(static sample => sample.BaseName).Should().Equal("drum-hitnormal");
        hits[2].Samples.Select(static sample => sample.BaseName).Should().Equal("drum-hitnormal", "soft-hitclap");

        var ticks = events.Where(static hit => hit.Samples.Any(static sample => sample.SoundName == "slidertick")).ToArray();
        ticks.Select(static hit => hit.Time.TotalMilliseconds).Should().Equal(1500, 2500);
        ticks[0].Samples.Single().BaseName.Should().Be("soft-slidertick");
        ticks[1].Samples.Single().BaseName.Should().Be("drum-slidertick", "the inherited point at 2000 switches to drum");
    }

    [Fact]
    public void Spinner_PlaysAtItsEnd()
    {
        var events = HitsoundTimelineBuilder.Build(lines(header + """
            [HitObjects]
            256,192,1000,12,4,4000,0:0:0:0:
            """));

        var hit = events.Should().ContainSingle().Which;
        hit.Time.Should().Be(TimeSpan.FromMilliseconds(4000));
        hit.Samples.Select(static sample => sample.BaseName).Should().Equal("drum-hitnormal", "drum-hitfinish");
    }

    [Fact]
    public void Events_AreSortedByTime()
    {
        var events = HitsoundTimelineBuilder.Build(lines(header + """
            [HitObjects]
            100,100,3000,1,0,0:0:0:0:
            100,100,1000,1,0,0:0:0:0:
            """));

        events.Select(static hit => hit.Time.TotalMilliseconds).Should().BeInAscendingOrder();
    }

    [Fact]
    public void SampleResolver_FollowsBeatmapThenSkinThenDefaultOrder()
    {
        var beatmap = new FakeSource(("soft-hitnormal2.wav", [1]), ("soft-hitnormal.wav", [2]), ("custom.wav", [3]));
        var skin = new FakeSource(("soft-hitnormal.wav", [4]), ("soft-hitclap.wav", [5]));
        var defaults = new FakeSource(("soft-hitnormal.wav", [6]), ("soft-hitclap.wav", [7]), ("soft-hitfinish.wav", [8]));
        var resolver = new HitsoundSampleResolver(beatmap, [skin, defaults]);

        resolver.Resolve(new HitsoundSample("soft", "hitnormal", 2, 1, null)).Should().Equal(1);
        resolver.Resolve(new HitsoundSample("soft", "hitnormal", 1, 1, null)).Should().Equal(2);
        resolver.Resolve(new HitsoundSample("soft", "hitnormal", 0, 1, null)).Should().Equal(new byte[] { 4 }, "index 0 skips the beatmap folder and uses the skin");
        resolver.Resolve(new HitsoundSample("soft", "hitnormal", 9, 1, null)).Should().Equal(new byte[] { 4 }, "a missing indexed sample falls back to the skin");
        resolver.Resolve(new HitsoundSample("soft", "hitclap", 1, 1, null)).Should().Equal(5);
        resolver.Resolve(new HitsoundSample("soft", "hitfinish", 1, 1, null)).Should().Equal(8);
        resolver.Resolve(new HitsoundSample("soft", "custom", 1, 1, "custom.wav")).Should().Equal(3);
        resolver.Resolve(new HitsoundSample("soft", "custom", 1, 1, "missing.wav")).Should().BeNull("explicit files never fall back to the skin");
    }

    [Fact]
    public void DirectorySource_FindsSamplesCaseInsensitivelyWithAnyAudioExtension()
    {
        using var directory = new TestDirectory();
        directory.CreateFile("Normal-HitClap.OGG");
        var source = new DirectorySampleFileSource(directory.Path);

        source.Read("normal-hitclap").Should().NotBeNull();
        source.Read("normal-hitfinish").Should().BeNull();
    }

    [Fact]
    public void ManagedResourceSource_ExtractsEmbeddedResourcesWithoutLoadingTheAssembly()
    {
        var source = new ManagedResourceSampleFileSource(typeof(HitsoundTimelineBuilderTests).Assembly.Location, "OsuMusicPlayer.Core.Tests.Resources.");

        var bytes = source.Read("soft-hitnormal");

        bytes.Should().NotBeNull();
        System.Text.Encoding.ASCII.GetString(bytes!).Should().Be("RIFF-fake-sample");
        source.Read("missing").Should().BeNull();
    }

    [Fact]
    public void StableSkinDirectory_IsReadFromUserConfig()
    {
        using var directory = new TestDirectory();
        directory.CreateDirectory("Skins", "My Skin");
        directory.CreateFile("osu!.someone.cfg", "Fullscreen = 1\r\nSkin = My Skin\r\n");

        HitsoundSampleSourceFactory.FindStableSkinDirectory(directory.Path).Should().Be(Path.Combine(directory.Path, "Skins", "My Skin"));

        File.WriteAllText(Path.Combine(directory.Path, "osu!.someone.cfg"), "Skin = ..\\..\\escape\r\n");
        HitsoundSampleSourceFactory.FindStableSkinDirectory(directory.Path).Should().BeNull();
    }

    private static IEnumerable<string> lines(string text) => text.Split('\n').Select(static line => line.TrimEnd('\r'));

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
