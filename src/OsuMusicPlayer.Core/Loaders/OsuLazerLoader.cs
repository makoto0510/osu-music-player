using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OsuMusicPlayer.Core.Models;
using Realms.Exceptions;

namespace OsuMusicPlayer.Core.Loaders;

public sealed class OsuLazerLoader : IBeatmapLoader
{
    private readonly ILazerRealmReader realmReader;
    private readonly ILogger<OsuLazerLoader> logger;

    public OsuLazerLoader(ILogger<OsuLazerLoader>? logger = null)
        : this(new LazerRealmReader(), logger)
    {
    }

    internal OsuLazerLoader(ILazerRealmReader realmReader, ILogger<OsuLazerLoader>? logger = null)
    {
        this.realmReader = realmReader ?? throw new ArgumentNullException(nameof(realmReader));
        this.logger = logger ?? NullLogger<OsuLazerLoader>.Instance;
    }

    public OsuInstallationKind Kind => OsuInstallationKind.Lazer;

    public async Task<IReadOnlyList<UnifiedBeatmapSet>> LoadAsync(
        string installationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationPath);

        var root = Path.GetFullPath(installationPath);
        var realmPath = Path.Combine(root, "client.realm");
        var filesPath = Path.Combine(root, "files");
        if (!File.Exists(realmPath) || !Directory.Exists(filesPath))
        {
            return [];
        }

        IReadOnlyList<LazerBeatmapSetData> rawSets;
        try
        {
            rawSets = await realmReader.ReadAsync(realmPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is RealmException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogWarning(exception, "Could not read lazer database at {RealmPath}.", realmPath);
            return [];
        }

        var results = new List<UnifiedBeatmapSet>();
        foreach (var set in rawSets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var files = new LazerHashFileResolver(filesPath, set.FileHashes);
                var audioPath = files.Resolve(set.AudioFileName);
                if (audioPath is null)
                {
                    continue;
                }

                results.Add(new UnifiedBeatmapSet
                {
                    Id = set.Id,
                    OnlineId = positiveOrNull(set.OnlineId),
                    Title = set.Title,
                    TitleUnicode = set.TitleUnicode,
                    Artist = set.Artist,
                    ArtistUnicode = set.ArtistUnicode,
                    Creator = set.Creator,
                    AudioFilePath = audioPath,
                    BackgroundFilePath = files.Resolve(set.BackgroundFileName),
                    Source = BeatmapSource.Lazer,
                    Files = files,
                    Beatmaps = set.Beatmaps.Select(beatmap => mapBeatmap(beatmap, filesPath)).ToArray(),
                });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                logger.LogWarning(exception, "Skipped corrupt lazer beatmap set {SetId}.", set.Id);
            }
        }

        return results;
    }

    internal static string? ResolveAssetPath(
        string filesPath,
        string fileName,
        IReadOnlyDictionary<string, string> fileHashes) =>
        new LazerHashFileResolver(filesPath, fileHashes).Resolve(fileName);

    private static UnifiedBeatmap mapBeatmap(LazerBeatmapData beatmap, string filesPath)
    {
        var length = TimeSpan.FromMilliseconds(Math.Max(0, beatmap.Length));
        return new UnifiedBeatmap
        {
            Id = beatmap.Id,
            OnlineId = positiveOrNull(beatmap.OnlineId),
            DifficultyName = beatmap.DifficultyName,
            Ruleset = mapRuleset(beatmap.RulesetShortName, beatmap.RulesetOnlineId),
            Md5Hash = string.IsNullOrWhiteSpace(beatmap.Md5Hash) ? null : beatmap.Md5Hash,
            // lazer stores the .osu file itself in the hash store under Beatmap.Hash.
            BeatmapFilePath = LazerHashFileResolver.BuildHashPath(filesPath, beatmap.Hash),
            BPM = beatmap.Bpm,
            Length = length,
            PreviewTime = OsuStableLoader.ResolvePreviewTime(beatmap.PreviewTimeMs, length),
            Tags = beatmap.Tags,
            StarRating = double.IsFinite(beatmap.StarRating) && beatmap.StarRating > 0 ? beatmap.StarRating : 0,
            CircleSize = beatmap.CircleSize,
            ApproachRate = beatmap.ApproachRate,
            DrainRate = beatmap.DrainRate,
            OverallDifficulty = beatmap.OverallDifficulty,
        };
    }

    private static OsuRuleset mapRuleset(string? shortName, int onlineId) => shortName?.ToLowerInvariant() switch
    {
        "osu" => OsuRuleset.Osu,
        "taiko" => OsuRuleset.Taiko,
        "fruits" => OsuRuleset.Catch,
        "mania" => OsuRuleset.Mania,
        _ => onlineId is >= 0 and <= 3 ? (OsuRuleset)onlineId : OsuRuleset.Unknown,
    };

    private static long? positiveOrNull(int value) => value > 0 ? value : null;
}
