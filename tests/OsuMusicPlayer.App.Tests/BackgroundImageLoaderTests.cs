using Avalonia.Media.Imaging;
using OsuMusicPlayer.App.Services;
using OsuMusicPlayer.App.ViewModels;
using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.App.Tests;

public sealed class BackgroundImageLoaderTests
{
    [Fact]
    public async Task LoadAsync_MissingOrCorruptImage_ReturnsFallbackNull()
    {
        var loader = new BackgroundImageLoader();
        var corruptPath = Path.Combine(Path.GetTempPath(), $"osu-player-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(corruptPath, [1, 2, 3, 4]);
        try
        {
            (await loader.LoadAsync(corruptPath)).Should().BeNull();
            (await loader.LoadAsync(corruptPath + ".missing")).Should().BeNull();
        }
        finally
        {
            File.Delete(corruptPath);
        }
    }

    [Fact]
    public async Task TrackItem_LoadsBackgroundLazilyAndOnlyOnce()
    {
        var loader = new CountingImageLoader();
        using var track = new TrackItemViewModel(createSet(), loader);

        loader.Calls.Should().Be(0);
        await track.BackgroundImage;
        await track.BackgroundImage;

        loader.Calls.Should().Be(1);

        await track.LargeBackgroundImage;
        await track.LargeBackgroundImage;

        loader.Calls.Should().Be(2, "the detail image is decoded separately at a larger size");
        loader.Heights.Should().Equal(96, 420);
    }

    private static UnifiedBeatmapSet createSet() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Title",
        TitleUnicode = string.Empty,
        Artist = "Artist",
        ArtistUnicode = string.Empty,
        Creator = "Mapper",
        BackgroundFilePath = "background.png",
        Beatmaps = [],
    };

    private sealed class CountingImageLoader : IBackgroundImageLoader
    {
        public int Calls { get; private set; }

        public Task<Bitmap?> LoadAsync(string? path, CancellationToken cancellationToken = default, int decodeHeight = 96)
        {
            Calls++;
            Heights.Add(decodeHeight);
            return Task.FromResult<Bitmap?>(null);
        }

        public List<int> Heights { get; } = [];
    }
}
