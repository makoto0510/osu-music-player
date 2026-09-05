using OsuParsers.Database.Objects;
using OsuParsers.Decoders;

namespace OsuMusicPlayer.Core.Loaders;

internal interface IStableDatabaseReader
{
    IReadOnlyList<DbBeatmap> Read(string databasePath);

    BeatmapEventAssets ReadEventAssets(string beatmapPath);
}

internal sealed class StableDatabaseReader : IStableDatabaseReader
{
    public IReadOnlyList<DbBeatmap> Read(string databasePath)
    {
        using var stream = new FileStream(
            databasePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        return DatabaseDecoder.DecodeOsu(stream).Beatmaps;
    }

    public BeatmapEventAssets ReadEventAssets(string beatmapPath) => OsuBeatmapFileParser.ReadEventAssets(beatmapPath);
}
