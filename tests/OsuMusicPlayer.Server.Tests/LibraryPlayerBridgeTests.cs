using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.Server.Tests;

public sealed class LibraryPlayerBridgeTests
{
    [Fact]
    public async Task Bridge_ExposesLibraryWithoutPlaybackAndTracksFavouritesInMemory()
    {
        var set = new UnifiedBeatmapSet
        {
            Id = Guid.NewGuid(),
            Title = "Senkou",
            TitleUnicode = "閃光",
            Artist = "[Alexandros]",
            ArtistUnicode = string.Empty,
            Creator = "AdveNt",
            AudioFilePath = "audio.mp3",
            BackgroundFilePath = "bg.jpg",
            Source = BeatmapSource.Lazer,
            Beatmaps = [new UnifiedBeatmap { Id = Guid.NewGuid(), DifficultyName = "Insane", Tags = "", BPM = 208, Length = TimeSpan.FromSeconds(257) }],
        };
        var silent = set with { Id = Guid.NewGuid(), AudioFilePath = null };
        var bridge = new LibraryPlayerBridge([set, silent]);

        bridge.TrackCount.Should().Be(1, "sets without audio cannot be streamed");
        var tracks = await bridge.GetTracksAsync(CancellationToken.None);
        tracks.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Title = "閃光", TitleRomanised = "Senkou", Bpm = 208d, HasBackground = true, Source = "lazer" });

        (await bridge.PlayAsync(set.Id, CancellationToken.None)).Should().BeFalse("a headless host has no player");
        (await bridge.GetStateAsync(CancellationToken.None)).Current.Should().BeNull();
        (await bridge.ToggleFavouriteAsync(set.Id, CancellationToken.None)).Should().BeTrue();
        (await bridge.GetTrackAsync(set.Id, CancellationToken.None))!.IsFavourite.Should().BeTrue();
        (await bridge.ToggleFavouriteAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeFalse();
    }
}
