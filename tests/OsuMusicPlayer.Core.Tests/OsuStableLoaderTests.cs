using System.Runtime.InteropServices;
using FluentAssertions;
using OsuMusicPlayer.Core.Loaders;
using OsuMusicPlayer.Core.Models;
using OsuParsers.Database.Objects;
using OsuParsers.Enums;

namespace OsuMusicPlayer.Core.Tests;

public sealed class OsuStableLoaderTests
{
    [Fact]
    public void StableDatabaseReader_ExtractsBackgroundWithoutParsingBrokenHitObjects()
    {
        using var directory = new TestDirectory();
        var beatmapPath = System.IO.Path.Combine(directory.Path, "map.osu");
        File.WriteAllText(
            beatmapPath,
            "osu file format v14\n[Events]\n0,0,\"background, image.jpg\",0,0\n[HitObjects]\nthis is deliberately invalid");

        var result = new StableDatabaseReader().ReadEventAssets(beatmapPath);

        result.BackgroundFileName.Should().Be("background, image.jpg");
        result.VideoFileName.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_MapsAndGroupsBeatmapsWhileIgnoringBrokenBackground()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        using var directory = new TestDirectory();
        directory.CreateFile("osu!.db");
        directory.CreateDirectory("Songs", "set-folder");
        var audioPath = directory.CreateFile("Songs\\set-folder\\audio.mp3");
        var easyPath = directory.CreateFile("Songs\\set-folder\\easy.osu");
        directory.CreateFile("Songs\\set-folder\\hard.osu");
        var reader = new FakeStableReader(
        [
            createEntry(1, "Easy", "easy.osu"),
            createEntry(2, "Hard", "hard.osu"),
        ]);
        var loader = new OsuStableLoader(reader);

        var result = await loader.LoadAsync(directory.Path);

        result.Should().ContainSingle();
        result[0].Should().BeEquivalentTo(new
        {
            OnlineId = (long?)100,
            Title = "Song",
            Artist = "Artist",
            Creator = "Mapper",
            AudioFilePath = audioPath,
            BackgroundFilePath = (string?)null,
            Source = BeatmapSource.Stable,
        });
        result[0].Files.Should().NotBeNull();
        result[0].Files!.Resolve("audio.mp3").Should().Be(audioPath);
        result[0].Files!.Resolve("..\\escape.mp3").Should().BeNull();
        result[0].Beatmaps.Should().HaveCount(2);
        result[0].Beatmaps[0].Should().BeEquivalentTo(new
        {
            Ruleset = OsuRuleset.Osu,
            StarRating = 5.5,
            Length = TimeSpan.FromSeconds(90),
            PreviewTime = TimeSpan.FromSeconds(30),
            CircleSize = 4d,
            ApproachRate = 9d,
            DrainRate = 6d,
            OverallDifficulty = 8d,
            BeatmapFilePath = easyPath,
            Md5Hash = 1.ToString("x32"),
        });
        result[0].Beatmaps[0].BPM.Should().BeApproximately(180, 0.001);
    }

    [Fact]
    public async Task LoadAsync_GroupsByAudioFileSoLocalDifficultiesJoinTheirOnlineSet()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        using var directory = new TestDirectory();
        directory.CreateFile("osu!.db");
        directory.CreateDirectory("Songs", "set-folder");
        var audioPath = directory.CreateFile("Songs\\set-folder\\audio.mp3");
        var otherAudioPath = directory.CreateFile("Songs\\set-folder\\sped up.mp3");
        var local = createEntry(0, "My local diff", "local.osu");
        local.BeatmapSetId = -1;
        var otherAudio = createEntry(3, "Other audio", "other.osu");
        otherAudio.AudioFileName = "sped up.mp3";
        var loader = new OsuStableLoader(new FakeStableReader(
        [
            local,
            createEntry(1, "Easy", "easy.osu"),
            otherAudio,
        ]));

        var result = await loader.LoadAsync(directory.Path);

        result.Should().HaveCount(2);
        var main = result.Single(set => set.AudioFilePath == audioPath);
        main.OnlineId.Should().Be(100);
        main.Beatmaps.Select(static beatmap => beatmap.DifficultyName).Should().BeEquivalentTo("My local diff", "Easy");
        result.Single(set => set.AudioFilePath == otherAudioPath).Beatmaps.Should().ContainSingle();
    }

    [Fact]
    public async Task LoadAsync_SkipsSetWhenAudioIsMissing()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        using var directory = new TestDirectory();
        directory.CreateFile("osu!.db");
        directory.CreateDirectory("Songs", "set-folder");
        var loader = new OsuStableLoader(new FakeStableReader([createEntry(1, "Easy", "easy.osu")]));

        var result = await loader.LoadAsync(directory.Path);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_UsesRulesetSpecificStarRatingAndDefaultPreviewPoint()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        using var directory = new TestDirectory();
        directory.CreateFile("osu!.db");
        directory.CreateDirectory("Songs", "set-folder");
        directory.CreateFile("Songs\\set-folder\\audio.mp3");
        var taiko = createEntry(1, "Oni", "oni.osu");
        taiko.Ruleset = Ruleset.Taiko;
        taiko.TaikoStarRating = new Dictionary<Mods, double> { [Mods.None] = 3.25 };
        taiko.AudioPreviewTime = -1;
        var loader = new OsuStableLoader(new FakeStableReader([taiko]));

        var result = await loader.LoadAsync(directory.Path);

        var beatmap = result.Should().ContainSingle().Which.Beatmaps.Should().ContainSingle().Subject;
        beatmap.Ruleset.Should().Be(OsuRuleset.Taiko);
        beatmap.StarRating.Should().Be(3.25);
        beatmap.PreviewTime.Should().Be(TimeSpan.FromSeconds(36), "osu! previews from 40% when no preview point is set");
        beatmap.BeatmapFilePath.Should().EndWith("oni.osu", "the path is kept and only opened when media is needed");
    }

    [Fact]
    public void CalculateMostCommonBpm_ConvertsBeatLengthAndPrefersLongestSection()
    {
        DbTimingPoint[] points =
        [
            new() { BPM = 500, Offset = 0, Inherited = false }, // 120 BPM for 1 s
            new() { BPM = -100, Offset = 500, Inherited = true }, // inherited points never count
            new() { BPM = 300, Offset = 1_000, Inherited = false }, // 200 BPM for 9 s
        ];

        OsuStableLoader.CalculateMostCommonBpm(points, 10_000).Should().BeApproximately(200, 0.001);
    }

    [Fact]
    public void CalculateMostCommonBpm_HandlesMissingOrInvalidTimingPoints()
    {
        OsuStableLoader.CalculateMostCommonBpm(null, 1_000).Should().Be(0);
        OsuStableLoader.CalculateMostCommonBpm([], 1_000).Should().Be(0);
        OsuStableLoader.CalculateMostCommonBpm([new DbTimingPoint { BPM = 0, Offset = 0, Inherited = false }], 1_000).Should().Be(0);
        OsuStableLoader.CalculateMostCommonBpm([new DbTimingPoint { BPM = 400, Offset = 5_000, Inherited = false }], 0).Should().BeApproximately(150, 0.001);
    }

    private static DbBeatmap createEntry(int beatmapId, string difficulty, string fileName)
    {
        var entry = new DbBeatmap
        {
            Artist = "Artist",
            ArtistUnicode = "Artist Unicode",
            Title = "Song",
            TitleUnicode = "Song Unicode",
            Creator = "Mapper",
            Difficulty = difficulty,
            AudioFileName = "audio.mp3",
            MD5Hash = beatmapId.ToString("x32"),
            FileName = fileName,
            Ruleset = Ruleset.Standard,
            CircleSize = 4,
            ApproachRate = 9,
            HPDrain = 6,
            OverallDifficulty = 8,
            TotalTime = 90_000,
            AudioPreviewTime = 30_000,
            BeatmapId = beatmapId,
            BeatmapSetId = 100,
            Tags = "tag one",
            FolderName = "set-folder",
        };
        entry.StandardStarRating = new Dictionary<Mods, double> { [Mods.None] = 5.5 };
        entry.TimingPoints.Add(new DbTimingPoint { BPM = 60_000d / 180, Offset = 0, Inherited = false });
        return entry;
    }

    private sealed class FakeStableReader(IReadOnlyList<DbBeatmap> entries) : IStableDatabaseReader
    {
        public IReadOnlyList<DbBeatmap> Read(string databasePath) => entries;

        public BeatmapEventAssets ReadEventAssets(string beatmapPath) => throw new InvalidDataException("broken .osu file");
    }
}
