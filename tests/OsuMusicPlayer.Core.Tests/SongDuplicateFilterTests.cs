using FluentAssertions;
using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.Core.Tests;

public sealed class SongDuplicateFilterTests
{
    [Fact]
    public void MatchesNamesAndDurationAcrossMappersAndKeepsOtherVersions()
    {
        var first = song("Song", "Artist", 120);
        var duplicate = song(" song ", " ARTIST\t", 122) with { Creator = "Other mapper" };
        var extended = song("Song", "Artist", 180);
        var cover = song("Song", "Other artist", 120);
        var otherTitle = song("Other song", "Artist", 120);
        var result = SongDuplicateFilter.Apply([first, duplicate, extended, cover, otherTitle]);

        result.Should().HaveCount(4).And.Contain([extended, cover, otherTitle]);
        result.Count(set => set.Id == first.Id || set.Id == duplicate.Id).Should().Be(1);
        SongDuplicateFilter.Apply([otherTitle, cover, extended, duplicate, first])
            .Select(set => set.Id).Should().BeEquivalentTo(result.Select(set => set.Id));
    }

    [Fact]
    public void MissingMetadataAndUnknownLengthsAreNeverCollapsed()
    {
        UnifiedBeatmapSet[] sets =
        [
            song("", "Artist", 120), song("", "Artist", 120),
            song("Song", " ", 120), song("Song", " ", 120),
            song("Song", "Artist", 0), song("Song", "Artist", 0),
            song("Song", "Artist", 120) with { Beatmaps = [] },
        ];
        SongDuplicateFilter.Apply(sets).Should().Equal(sets);
    }

    [Fact]
    public void NearMatchChainsDoNotCollapseDistantDurations()
    {
        var first = song("Song", "Artist", 120) with { Id = new Guid("00000000-0000-0000-0000-000000000001") };
        var second = song("Song", "Artist", 122) with { Id = new Guid("00000000-0000-0000-0000-000000000002") };
        var third = song("Song", "Artist", 124) with { Id = new Guid("00000000-0000-0000-0000-000000000003") };
        SongDuplicateFilter.Apply([first, second, third]).Should().Equal(first, third);
    }

    private static UnifiedBeatmapSet song(string title, string artist, int seconds) =>
        ModelFactory.Set(title: title, artist: artist,
            beatmaps: [ModelFactory.Beatmap() with { Length = TimeSpan.FromSeconds(seconds) }]);
}
