using FluentAssertions;
using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Core.Search;

namespace OsuMusicPlayer.Core.Tests;

public sealed class TrackSearchQueryTests
{
    private static readonly UnifiedBeatmapSet camellia = create("Camellia feat. Nanahira", "Exit This Earth's Atomosphere", "Mapper A", BeatmapSource.Both,
        ("Extra", OsuRuleset.Osu, 200, 7.1, 240, "electronic speedcore"),
        ("Taiko Oni", OsuRuleset.Taiko, 200, 5.2, 240, "electronic speedcore"));

    private static readonly UnifiedBeatmapSet yoasobi = create("YOASOBI", "Idol", "Mapper B", BeatmapSource.Lazer,
        ("Hard", OsuRuleset.Osu, 166, 3.4, 213, "anime jpop oshi no ko"));

    [Fact]
    public void PlainWords_MatchAnyFieldAndAllMustMatch()
    {
        TrackSearchQuery.Parse("camellia atomosphere").Matches(camellia).Should().BeTrue();
        TrackSearchQuery.Parse("camellia idol").Matches(camellia).Should().BeFalse();
        TrackSearchQuery.Parse("MAPPER b").Matches(yoasobi).Should().BeTrue();
        TrackSearchQuery.Parse("speedcore").Matches(camellia).Should().BeTrue("tags are searched");
        TrackSearchQuery.Parse("oni").Matches(camellia).Should().BeTrue("difficulty names are searched");
        TrackSearchQuery.Parse("   ").IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void FieldPrefixes_RestrictTheTermToOneField()
    {
        TrackSearchQuery.Parse("artist:yoasobi").Matches(yoasobi).Should().BeTrue();
        TrackSearchQuery.Parse("artist:idol").Matches(yoasobi).Should().BeFalse();
        TrackSearchQuery.Parse("title:idol").Matches(yoasobi).Should().BeTrue();
        TrackSearchQuery.Parse("mapper:\"mapper a\"").Matches(camellia).Should().BeTrue();
        TrackSearchQuery.Parse("tag:jpop").Matches(yoasobi).Should().BeTrue();
        TrackSearchQuery.Parse("tag:jpop").Matches(camellia).Should().BeFalse();
        TrackSearchQuery.Parse("source:stable").Matches(camellia).Should().BeTrue();
        TrackSearchQuery.Parse("source:stable").Matches(yoasobi).Should().BeFalse();
        TrackSearchQuery.Parse("mode:taiko").Matches(camellia).Should().BeTrue();
        TrackSearchQuery.Parse("mode:mania").Matches(camellia).Should().BeFalse();
        TrackSearchQuery.Parse("unknownkey:idol").Matches(yoasobi).Should().BeTrue("an unknown key is searched as plain text");
    }

    [Fact]
    public void NumericRanges_WorkForBpmStarsAndLength()
    {
        TrackSearchQuery.Parse("bpm:150-170").Matches(yoasobi).Should().BeTrue();
        TrackSearchQuery.Parse("bpm:150-170").Matches(camellia).Should().BeFalse();
        TrackSearchQuery.Parse("bpm:>190").Matches(camellia).Should().BeTrue();
        TrackSearchQuery.Parse("bpm:200").Matches(camellia).Should().BeTrue();
        TrackSearchQuery.Parse("stars:<4").Matches(yoasobi).Should().BeTrue();
        TrackSearchQuery.Parse("stars:>=7").Matches(camellia).Should().BeTrue();
        TrackSearchQuery.Parse("length:3:30-4:30").Matches(camellia).Should().BeTrue();
        TrackSearchQuery.Parse("length:<200").Matches(camellia).Should().BeFalse();
    }

    [Fact]
    public void NegationAndAlternatives()
    {
        TrackSearchQuery.Parse("-yoasobi").Matches(yoasobi).Should().BeFalse();
        TrackSearchQuery.Parse("-yoasobi").Matches(camellia).Should().BeTrue();
        TrackSearchQuery.Parse("artist:yoasobi|camellia").Matches(camellia).Should().BeTrue();
        TrackSearchQuery.Parse("artist:yoasobi|camellia").Matches(yoasobi).Should().BeTrue();
        TrackSearchQuery.Parse("-tag:anime mode:osu").Matches(camellia).Should().BeTrue();
        TrackSearchQuery.Parse("-tag:anime mode:osu").Matches(yoasobi).Should().BeFalse();
    }

    [Fact]
    public void GenreAndLanguage_ComeFromOptionalOnlineMetadata()
    {
        TrackSearchQuery.Parse("genre:anime").Matches(yoasobi, "Anime", "Japanese").Should().BeTrue();
        TrackSearchQuery.Parse("genre:anime").Matches(yoasobi).Should().BeFalse("nothing is known offline");
        TrackSearchQuery.Parse("language:japanese -genre:rock").Matches(yoasobi, "Anime", "Japanese").Should().BeTrue();
    }

    private static UnifiedBeatmapSet create(string artist, string title, string creator, BeatmapSource source, params (string Name, OsuRuleset Ruleset, double Bpm, double Stars, int Seconds, string Tags)[] diffs) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        TitleUnicode = string.Empty,
        Artist = artist,
        ArtistUnicode = string.Empty,
        Creator = creator,
        Source = source,
        Beatmaps = diffs.Select(diff => new UnifiedBeatmap
        {
            Id = Guid.NewGuid(),
            DifficultyName = diff.Name,
            Ruleset = diff.Ruleset,
            BPM = diff.Bpm,
            StarRating = diff.Stars,
            Length = TimeSpan.FromSeconds(diff.Seconds),
            Tags = diff.Tags,
        }).ToArray(),
    };
}
