using FluentAssertions;
using OsuMusicPlayer.Core.Loaders;
using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.Core.Tests;

public sealed class OsuLazerLoaderTests
{
    private const string hash = "1a47929b6056d34d25a95eeb2012395ceed66af6f40cc37c898a08482d6325d2";
    private const string beatmap_hash = "2b47929b6056d34d25a95eeb2012395ceed66af6f40cc37c898a08482d6325d3";

    [Fact]
    public void ResolveAssetPath_UsesCurrentThreeLevelLayout()
    {
        using var directory = new TestDirectory();
        var files = directory.CreateDirectory("files");
        var expected = directory.CreateFile($"files\\1\\1a\\{hash}");

        var result = OsuLazerLoader.ResolveAssetPath(files, "audio.mp3", new Dictionary<string, string> { ["audio.mp3"] = hash });

        result.Should().Be(expected);
    }

    [Fact]
    public void ResolveAssetPath_FallsBackToDocumentedTwoLevelLayout()
    {
        using var directory = new TestDirectory();
        var files = directory.CreateDirectory("files");
        var expected = directory.CreateFile($"files\\1a\\{hash}");

        var result = OsuLazerLoader.ResolveAssetPath(files, "audio.mp3", new Dictionary<string, string> { ["audio.mp3"] = hash.ToUpperInvariant() });

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("../escape")]
    public void ResolveAssetPath_RejectsInvalidHashes(string invalidHash)
    {
        using var directory = new TestDirectory();
        var files = directory.CreateDirectory("files");

        var result = OsuLazerLoader.ResolveAssetPath(files, "audio.mp3", new Dictionary<string, string> { ["audio.mp3"] = invalidHash });

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_MapsRealmDataAndSkipsSetWithMissingAudio()
    {
        using var directory = new TestDirectory();
        directory.CreateFile("client.realm");
        var files = directory.CreateDirectory("files");
        var expectedAudio = directory.CreateFile($"files\\1\\1a\\{hash}");
        var valid = createSet("audio.mp3", new Dictionary<string, string> { ["audio.mp3"] = hash });
        var missingAudio = createSet("missing.mp3", new Dictionary<string, string>());
        var loader = new OsuLazerLoader(new FakeLazerReader([valid, missingAudio]));

        var result = await loader.LoadAsync(directory.Path);

        result.Should().ContainSingle();
        result[0].AudioFilePath.Should().Be(expectedAudio);
        result[0].Source.Should().Be(BeatmapSource.Lazer);
        result[0].Files.Should().NotBeNull();
        result[0].Files!.Resolve("AUDIO.MP3").Should().Be(expectedAudio);
        result[0].Beatmaps.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            OnlineId = (long?)456,
            DifficultyName = "Insane",
            Ruleset = OsuRuleset.Taiko,
            Md5Hash = "abc",
            BeatmapFilePath = Path.Combine(files, "2", "2b", beatmap_hash),
            BPM = 200d,
            Length = TimeSpan.FromSeconds(120),
            PreviewTime = TimeSpan.FromSeconds(45),
            Tags = "tag",
            StarRating = 5.25,
            CircleSize = 4d,
            ApproachRate = 9d,
            DrainRate = 7d,
            OverallDifficulty = 9d,
        });
    }

    [Fact]
    public async Task LoadAsync_DefaultsPreviewTo40PercentAndMapsUnknownRulesets()
    {
        using var directory = new TestDirectory();
        directory.CreateFile("client.realm");
        directory.CreateDirectory("files");
        directory.CreateFile($"files\\1\\1a\\{hash}");
        var set = createSet("audio.mp3", new Dictionary<string, string> { ["audio.mp3"] = hash }) with
        {
            Beatmaps =
            [
                new LazerBeatmapData(Guid.NewGuid(), 1, "Easy", 100, 1000, string.Empty, 1, 1)
                {
                    RulesetShortName = "custom",
                    RulesetOnlineId = 42,
                    PreviewTimeMs = -1,
                },
                new LazerBeatmapData(Guid.NewGuid(), 2, "Keys", 100, 1000, string.Empty, 1, 1)
                {
                    RulesetShortName = "",
                    RulesetOnlineId = 3,
                    PreviewTimeMs = -1,
                },
            ],
        };

        var result = await new OsuLazerLoader(new FakeLazerReader([set])).LoadAsync(directory.Path);

        var beatmaps = result.Should().ContainSingle().Which.Beatmaps;
        beatmaps.Should().HaveCount(2);
        beatmaps[0].PreviewTime.Should().Be(TimeSpan.FromMilliseconds(400));
        beatmaps[0].Ruleset.Should().Be(OsuRuleset.Unknown);
        beatmaps[0].Md5Hash.Should().BeNull();
        beatmaps[0].BeatmapFilePath.Should().BeNull("no .osu hash was stored");
        beatmaps[1].Ruleset.Should().Be(OsuRuleset.Mania);
    }

    private static LazerBeatmapSetData createSet(string audioFile, IReadOnlyDictionary<string, string> files) => new(
        Guid.NewGuid(),
        123,
        "Title",
        "Title Unicode",
        "Artist",
        "Artist Unicode",
        "Mapper",
        audioFile,
        string.Empty,
        files,
        [
            new LazerBeatmapData(Guid.NewGuid(), 456, "Insane", 200, 120_000, "tag", 7, 9)
            {
                RulesetShortName = "taiko",
                RulesetOnlineId = 1,
                Md5Hash = "abc",
                Hash = beatmap_hash,
                PreviewTimeMs = 45_000,
                StarRating = 5.25,
                CircleSize = 4,
                ApproachRate = 9,
            },
        ]);

    private sealed class FakeLazerReader(IReadOnlyList<LazerBeatmapSetData> sets) : ILazerRealmReader
    {
        public Task<IReadOnlyList<LazerBeatmapSetData>> ReadAsync(string realmPath, CancellationToken cancellationToken) =>
            Task.FromResult(sets);
    }
}
