using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OsuMusicPlayer.Core.Models;
using OsuParsers.Database.Objects;
using OsuParsers.Enums;

namespace OsuMusicPlayer.Core.Loaders;

public sealed class OsuStableLoader : IBeatmapLoader
{
    private readonly IStableDatabaseReader databaseReader;
    private readonly ILogger<OsuStableLoader> logger;

    public OsuStableLoader(ILogger<OsuStableLoader>? logger = null)
        : this(new StableDatabaseReader(), logger)
    {
    }

    internal OsuStableLoader(IStableDatabaseReader databaseReader, ILogger<OsuStableLoader>? logger = null)
    {
        this.databaseReader = databaseReader ?? throw new ArgumentNullException(nameof(databaseReader));
        this.logger = logger ?? NullLogger<OsuStableLoader>.Instance;
    }

    public OsuInstallationKind Kind => OsuInstallationKind.Stable;

    public Task<IReadOnlyList<UnifiedBeatmapSet>> LoadAsync(
        string installationPath,
        CancellationToken cancellationToken = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Task.FromResult<IReadOnlyList<UnifiedBeatmapSet>>([]);
        }

        return Task.Run(() => load(installationPath, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Calculates the BPM the way osu! displays it: the beat length that covers the
    /// longest part of the map wins. osu!.db stores uninherited timing points as
    /// milliseconds per beat, not as BPM.
    /// </summary>
    internal static double CalculateMostCommonBpm(IEnumerable<DbTimingPoint>? timingPoints, int totalTimeMs)
    {
        var points = timingPoints?
            .Where(static point => !point.Inherited && double.IsFinite(point.BPM) && point.BPM > 0 && double.IsFinite(point.Offset))
            .OrderBy(static point => point.Offset)
            .ToArray();
        if (points is null || points.Length == 0)
        {
            return 0;
        }

        var end = Math.Max(totalTimeMs, points[^1].Offset);
        var durations = new Dictionary<double, double>();
        for (var i = 0; i < points.Length; i++)
        {
            var next = i + 1 < points.Length ? points[i + 1].Offset : end;
            var beatLength = Math.Round(points[i].BPM, 3);
            durations[beatLength] = durations.GetValueOrDefault(beatLength) + Math.Max(0, next - points[i].Offset);
        }

        var mostCommon = durations.MaxBy(static pair => pair.Value).Key;
        return mostCommon > 0 ? 60_000 / mostCommon : 0;
    }

    /// <summary>osu! uses -1 to mean "no preview point set"; the game then previews from 40% in.</summary>
    internal static TimeSpan ResolvePreviewTime(double previewTimeMs, TimeSpan length) =>
        previewTimeMs >= 0 && double.IsFinite(previewTimeMs)
            ? TimeSpan.FromMilliseconds(previewTimeMs)
            : TimeSpan.FromTicks((long)(length.Ticks * 0.4));

    private IReadOnlyList<UnifiedBeatmapSet> load(string installationPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationPath);

        var root = Path.GetFullPath(installationPath);
        var databasePath = Path.Combine(root, "osu!.db");
        var songsPath = Path.Combine(root, "Songs");
        if (!File.Exists(databasePath) || !Directory.Exists(songsPath))
        {
            return [];
        }

        IReadOnlyList<DbBeatmap> entries;
        try
        {
            entries = databaseReader.Read(databasePath);
        }
        catch (Exception exception) when (isRecoverable(exception))
        {
            logger.LogWarning(exception, "Could not read stable database at {DatabasePath}.", databasePath);
            return [];
        }

        // A music player track is one audio file. Difficulties that share a folder and an
        // audio file belong to the same track even when some of them have no online set id
        // (locally created difficulties) or were downloaded as a different online set.
        var mapped = new List<(string AudioPath, string FolderPath, DbBeatmap Entry)>();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var folderPath = resolveChildPath(songsPath, entry.FolderName);
                if (folderPath is null || !Directory.Exists(folderPath))
                {
                    continue;
                }

                var audioPath = resolveChildPath(folderPath, entry.AudioFileName);
                if (audioPath is null || !File.Exists(audioPath))
                {
                    continue;
                }

                mapped.Add((audioPath, folderPath, entry));
            }
            catch (Exception exception) when (isRecoverable(exception))
            {
                logger.LogWarning(exception, "Skipped an invalid stable beatmap database entry.");
            }
        }

        var results = new List<UnifiedBeatmapSet>();
        foreach (var group in mapped.GroupBy(static item => item.AudioPath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var primary = group.FirstOrDefault(static item => item.Entry.BeatmapSetId > 0);
                if (primary.Entry is null)
                {
                    primary = group.First();
                }

                var backgroundPath = resolveBackgroundPath(group, cancellationToken);
                var beatmaps = group.Select(item => mapBeatmap(item.Entry, item.FolderPath)).ToArray();
                results.Add(new UnifiedBeatmapSet
                {
                    Id = createGuid($"stable-set:{group.Key.ToUpperInvariant()}"),
                    OnlineId = positiveOrNull(primary.Entry.BeatmapSetId),
                    Title = primary.Entry.Title ?? string.Empty,
                    TitleUnicode = primary.Entry.TitleUnicode ?? string.Empty,
                    Artist = primary.Entry.Artist ?? string.Empty,
                    ArtistUnicode = primary.Entry.ArtistUnicode ?? string.Empty,
                    Creator = primary.Entry.Creator ?? string.Empty,
                    AudioFilePath = group.Key,
                    BackgroundFilePath = backgroundPath,
                    Source = BeatmapSource.Stable,
                    Files = new FolderFileResolver(primary.FolderPath),
                    Beatmaps = beatmaps,
                });
            }
            catch (Exception exception) when (isRecoverable(exception))
            {
                logger.LogWarning(exception, "Skipped corrupt stable beatmap set {AudioPath}.", group.Key);
            }
        }

        return results;
    }

    private string? resolveBackgroundPath(
        IEnumerable<(string AudioPath, string FolderPath, DbBeatmap Entry)> group,
        CancellationToken cancellationToken)
    {
        foreach (var item in group)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var beatmapPath = resolveChildPath(item.FolderPath, item.Entry.FileName);
                if (beatmapPath is null || !File.Exists(beatmapPath))
                {
                    continue;
                }

                var backgroundName = databaseReader.ReadEventAssets(beatmapPath).BackgroundFileName;
                var backgroundPath = resolveChildPath(item.FolderPath, backgroundName);
                if (backgroundPath is not null && File.Exists(backgroundPath))
                {
                    return backgroundPath;
                }
            }
            catch (Exception exception) when (isRecoverable(exception))
            {
                logger.LogWarning(exception, "Could not resolve background for {BeatmapFile}.", item.Entry.FileName);
            }
        }

        return null;
    }

    private static UnifiedBeatmap mapBeatmap(DbBeatmap entry, string folderPath)
    {
        var ruleset = mapRuleset(entry.Ruleset);
        var length = TimeSpan.FromMilliseconds(Math.Max(0, entry.TotalTime));
        var beatmapPath = resolveChildPath(folderPath, entry.FileName);
        return new UnifiedBeatmap
        {
            Id = createGuid($"stable-beatmap:{entry.MD5Hash ?? entry.FileName}"),
            OnlineId = positiveOrNull(entry.BeatmapId),
            DifficultyName = entry.Difficulty ?? string.Empty,
            Ruleset = ruleset,
            StarRating = readStarRating(entry, ruleset),
            BPM = CalculateMostCommonBpm(entry.TimingPoints, entry.TotalTime),
            Length = length,
            PreviewTime = ResolvePreviewTime(entry.AudioPreviewTime, length),
            Tags = entry.Tags ?? string.Empty,
            CircleSize = entry.CircleSize,
            ApproachRate = entry.ApproachRate,
            DrainRate = entry.HPDrain,
            OverallDifficulty = entry.OverallDifficulty,
            BeatmapFilePath = beatmapPath,
        };
    }

    private static OsuRuleset mapRuleset(Ruleset ruleset) => ruleset switch
    {
        Ruleset.Standard => OsuRuleset.Osu,
        Ruleset.Taiko => OsuRuleset.Taiko,
        Ruleset.Fruits => OsuRuleset.Catch,
        Ruleset.Mania => OsuRuleset.Mania,
        _ => OsuRuleset.Unknown,
    };

    private static double readStarRating(DbBeatmap entry, OsuRuleset ruleset)
    {
        var ratings = ruleset switch
        {
            OsuRuleset.Taiko => entry.TaikoStarRating,
            OsuRuleset.Catch => entry.CatchStarRating,
            OsuRuleset.Mania => entry.ManiaStarRating,
            _ => entry.StandardStarRating,
        };

        return ratings is not null && ratings.TryGetValue(Mods.None, out var rating) && double.IsFinite(rating) && rating > 0 ? rating : 0;
    }

    private static string? resolveChildPath(string parent, string? child)
    {
        if (string.IsNullOrWhiteSpace(child) || Path.IsPathRooted(child))
        {
            return null;
        }

        var parentPath = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var childPath = Path.GetFullPath(Path.Combine(parentPath, child));
        return childPath.StartsWith(parentPath, StringComparison.OrdinalIgnoreCase) ? childPath : null;
    }

    private static long? positiveOrNull(int value) => value > 0 ? value : null;

    private static Guid createGuid(string value)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(value), hash);
        return new Guid(hash[..16]);
    }

    private static bool isRecoverable(Exception exception) => exception is
        IOException or
        UnauthorizedAccessException or
        InvalidDataException or
        ArgumentException or
        NotSupportedException or
        EndOfStreamException or
        FormatException or
        OverflowException or
        IndexOutOfRangeException;
}
