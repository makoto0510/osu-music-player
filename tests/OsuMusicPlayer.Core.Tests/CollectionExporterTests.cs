using FluentAssertions;
using OsuMusicPlayer.Core.Collections;
using OsuParsers.Decoders;

namespace OsuMusicPlayer.Core.Tests;

public sealed class CollectionExporterTests
{
    [Fact]
    public void Write_ProducesAFileOsuParsersReadsBack()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "export", "collection.db");

        CollectionExporter.Write(path, "My export", ["AAA", "bbb", "aaa", " ", "ccc"], []);

        var database = DatabaseDecoder.DecodeCollection(path);
        database.Collections.Should().ContainSingle();
        database.Collections[0].Name.Should().Be("My export");
        database.Collections[0].MD5Hashes.Should().Equal("aaa", "bbb", "ccc");
    }

    [Fact]
    public void Write_RefusesPathsInsideAnOsuInstallation()
    {
        using var directory = new TestDirectory();
        var osuRoot = directory.CreateDirectory("osu!");
        var inside = Path.Combine(osuRoot, "collection.db");

        var action = () => CollectionExporter.Write(inside, "x", ["a"], [osuRoot]);

        action.Should().Throw<InvalidOperationException>();
        File.Exists(inside).Should().BeFalse();
        CollectionExporter.IsInside(Path.Combine(directory.Path, "elsewhere", "collection.db"), osuRoot).Should().BeFalse();
    }
}
