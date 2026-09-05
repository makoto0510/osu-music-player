using FluentAssertions;
using OsuMusicPlayer.Core.Loaders;
using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.Core.Tests;

public sealed class BeatmapMediaTests
{
    [Fact]
    public void ReadEventAssets_ParsesBackgroundAndVideoInBothSyntaxes()
    {
        const string text = """
            osu file format v14

            [General]
            AudioFilename: audio.mp3

            [Events]
            //Background and Video events
            Video,-320,"clip, final.mp4"
            0,0,"bg.jpg",0,0
            //Storyboard Layer 0 (Background)
            Sprite,Background,Centre,"sb\\thing.png",320,240
            [TimingPoints]
            0,300,4,2,0,100,1,0
            """;

        var result = OsuBeatmapFileParser.ReadEventAssets(new StringReader(text));

        result.BackgroundFileName.Should().Be("bg.jpg");
        result.VideoFileName.Should().Be("clip, final.mp4");
        result.VideoOffset.Should().Be(TimeSpan.FromMilliseconds(-320));
    }

    [Fact]
    public void ReadEventAssets_AcceptsNumericVideoKindAndUnquotedNames()
    {
        var result = OsuBeatmapFileParser.ReadEventAssets(new StringReader("[Events]\n1,0,video.avi\n0,0,bg.png\n"));

        result.VideoFileName.Should().Be("video.avi");
        result.BackgroundFileName.Should().Be("bg.png");
        result.VideoOffset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ReadEventAssets_ReturnsEmptyWhenNoEventsSection()
    {
        var result = OsuBeatmapFileParser.ReadEventAssets(new StringReader("[General]\nMode: 0\n"));

        result.Should().Be(BeatmapEventAssets.Empty);
    }

    [Fact]
    public async Task Resolver_FindsVideoAndStoryboardThroughTheSetFiles()
    {
        using var directory = new TestDirectory();
        var folder = directory.CreateDirectory("set");
        var videoPath = directory.CreateFile("set\\clip.mp4");
        var storyboardPath = directory.CreateFile("set\\Artist - Title (Mapper).osb");
        var brokenBeatmap = directory.CreateFile("set\\broken.osu", "[Events]\nVideo,0,\"missing.mp4\"\n");
        var beatmapPath = directory.CreateFile("set\\map.osu", "[Events]\nVideo,120,\"clip.mp4\"\n0,0,\"bg.jpg\"\n");
        var set = new UnifiedBeatmapSet
        {
            Id = Guid.NewGuid(),
            Title = "Title",
            TitleUnicode = string.Empty,
            Artist = "Artist",
            ArtistUnicode = string.Empty,
            Creator = "Mapper",
            Files = new FolderFileResolver(folder),
            Beatmaps =
            [
                new UnifiedBeatmap { Id = Guid.NewGuid(), DifficultyName = "Broken", Tags = string.Empty, BeatmapFilePath = brokenBeatmap },
                new UnifiedBeatmap { Id = Guid.NewGuid(), DifficultyName = "Normal", Tags = string.Empty, BeatmapFilePath = beatmapPath },
            ],
        };

        var media = await new BeatmapMediaResolver().ResolveAsync(set);

        media.VideoFilePath.Should().Be(videoPath);
        media.VideoOffset.Should().Be(TimeSpan.FromMilliseconds(120));
        media.StoryboardFilePath.Should().Be(storyboardPath);
        media.HasVideo.Should().BeTrue();
    }

    [Fact]
    public async Task Resolver_ReturnsNoneWithoutFileResolver()
    {
        var set = ModelFactory.Set(beatmaps: [ModelFactory.Beatmap()]);

        (await new BeatmapMediaResolver().ResolveAsync(set)).Should().Be(BeatmapMedia.None);
    }

    [Fact]
    public void LazerHashFileResolver_ResolvesCaseInsensitivelyAndListsNames()
    {
        using var directory = new TestDirectory();
        var files = directory.CreateDirectory("files");
        const string hash = "1a47929b6056d34d25a95eeb2012395ceed66af6f40cc37c898a08482d6325d2";
        var expected = directory.CreateFile($"files\\1\\1a\\{hash}");
        var resolver = new LazerHashFileResolver(files, new Dictionary<string, string> { ["Video.MP4"] = hash });

        resolver.Resolve("video.mp4").Should().Be(expected);
        resolver.Resolve("other.mp4").Should().BeNull();
        resolver.FileNames.Should().ContainSingle().Which.Should().Be("Video.MP4");
    }
}
