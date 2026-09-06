using System.Runtime.InteropServices;
using FluentAssertions;
using OsuMusicPlayer.Core.Hitsounds;
using OsuMusicPlayer.Core.Loaders;
using OsuParsers.Database;
using OsuParsers.Database.Objects;

namespace OsuMusicPlayer.Core.Tests;

public sealed class CollectionLoaderTests
{
    [Fact]
    public async Task StableLoader_ReadsCollectionDbWrittenByOsuParsers()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        using var directory = new TestDirectory();
        var database = new CollectionDatabase { OsuVersion = 20240101, CollectionCount = 2 };
        var favourites = new Collection { Name = "Favourites", Count = 3 }; // OsuParsers writes Count entries
        favourites.MD5Hashes.AddRange(["aaa", "bbb", "AAA"]);
        database.Collections.Add(favourites);
        database.Collections.Add(new Collection { Name = "Empty", Count = 0 });
        database.Save(Path.Combine(directory.Path, "collection.db"));

        var result = await new OsuStableCollectionLoader().LoadAsync(directory.Path);

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Favourites");
        result[0].Source.Should().Be(OsuInstallationKind.Stable);
        result[0].BeatmapMd5Hashes.Should().Equal("aaa", "bbb");
        result[1].BeatmapMd5Hashes.Should().BeEmpty();
    }

    [Fact]
    public async Task StableLoader_ReturnsEmptyWithoutDatabase()
    {
        using var directory = new TestDirectory();

        (await new OsuStableCollectionLoader().LoadAsync(directory.Path)).Should().BeEmpty();
    }

    [Fact]
    public async Task LazerLoader_MapsRealmCollections()
    {
        using var directory = new TestDirectory();
        directory.CreateFile("client.realm");
        var loader = new OsuLazerCollectionLoader(new FakeCollectionReader([new LazerCollectionData("Practice", ["x", "", "y", "x"])]));

        var result = await loader.LoadAsync(directory.Path);

        result.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Name = "Practice",
            Source = OsuInstallationKind.Lazer,
            BeatmapMd5Hashes = new[] { "x", "y" },
        });
    }

    [Fact]
    public void LazerSkin_IsReadFromGameIniAndMappedToBundledSampleSet()
    {
        using var directory = new TestDirectory();
        var id = Guid.NewGuid();
        directory.CreateFile("game.ini", $"Volume = 1\r\nSkin = {id}\r\n");

        HitsoundSampleSourceFactory.FindLazerSkinId(directory.Path).Should().Be(id);
        HitsoundSampleSourceFactory.ResourcePrefixForSkinName("osu! \"argon\" (2022)").Should().EndWith("Gameplay.Argon.");
        HitsoundSampleSourceFactory.ResourcePrefixForSkinName("osu! \"argon\" pro (2022)").Should().EndWith("Gameplay.ArgonPro.");
        HitsoundSampleSourceFactory.ResourcePrefixForSkinName("osu! \"triangles\" (2017)").Should().EndWith("Gameplay.");
    }

    [Fact]
    public void Factory_UsesLazerSkinFilesBeforeBundledDefaults()
    {
        using var directory = new TestDirectory();
        var id = Guid.NewGuid();
        var lazerRoot = directory.CreateDirectory("lazer");
        directory.CreateFile(Path.Combine("lazer", "game.ini"), $"Skin = {id}\r\n");
        directory.CreateFile(Path.Combine("lazer", "client.realm"));
        const string hash = "1a47929b6056d34d25a95eeb2012395ceed66af6f40cc37c898a08482d6325d2";
        directory.CreateFile(Path.Combine("lazer", "files", "1", "1a", hash), "skin sample");
        var reader = new FakeSkinReader(new LazerSkinData(id, "My skin", false, new Dictionary<string, string> { ["soft-hitnormal.wav"] = hash }));
        var factory = new HitsoundSampleSourceFactory(static () => null, reader);

        var resolver = factory.Create(ModelFactory.Set(), [new OsuInstallation(OsuInstallationKind.Lazer, lazerRoot)]);

        var bytes = resolver.Resolve(new Models.HitsoundSample("soft", "hitnormal", 0, 1, null));
        System.Text.Encoding.UTF8.GetString(bytes!).Should().Be("skin sample");
        reader.Reads.Should().Be(1);
        factory.Create(ModelFactory.Set(), [new OsuInstallation(OsuInstallationKind.Lazer, lazerRoot)]);
        reader.Reads.Should().Be(1, "the skin lookup is cached per installation");
    }

    private sealed class FakeCollectionReader(IReadOnlyList<LazerCollectionData> collections) : ILazerCollectionReader
    {
        public Task<IReadOnlyList<LazerCollectionData>> ReadCollectionsAsync(string realmPath, CancellationToken cancellationToken) => Task.FromResult(collections);
    }

    private sealed class FakeSkinReader(LazerSkinData skin) : ILazerSkinReader
    {
        public int Reads { get; private set; }

        public LazerSkinData? ReadSkin(string realmPath, Guid skinId)
        {
            Reads++;
            return skin.Id == skinId ? skin : null;
        }
    }
}
