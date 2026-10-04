using OsuMusicPlayer.Core.Loaders;
using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.Core;

public sealed class BeatmapManager(
    IEnumerable<IBeatmapLoader> loaders,
    IDuplicateDetector duplicateDetector)
{
    private readonly IReadOnlyList<IBeatmapLoader> loaders = loaders?.ToArray() ?? throw new ArgumentNullException(nameof(loaders));
    private readonly IDuplicateDetector duplicateDetector = duplicateDetector ?? throw new ArgumentNullException(nameof(duplicateDetector));

    public async Task<IReadOnlyList<UnifiedBeatmapSet>> LoadAsync(
        IEnumerable<OsuInstallation> installations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installations);
        cancellationToken.ThrowIfCancellationRequested();

        var tasks = installations.Select(installation => loadInstallationAsync(installation, cancellationToken));
        var loaded = (await Task.WhenAll(tasks).ConfigureAwait(false)).SelectMany(static sets => sets);

        // Libraries hold thousands of sets, so candidates are bucketed by online id and by
        // metadata key instead of comparing every pair.
        var merged = new List<UnifiedBeatmapSet>();
        var byOnlineId = new Dictionary<long, List<int>>();
        var byMetadataKey = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var candidate in loaded)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadataKey = duplicateDetector.CreateMetadataKey(candidate);
            var index = -1;
            if (candidate.OnlineId is > 0 && byOnlineId.TryGetValue(candidate.OnlineId.Value, out var onlineIndices))
            {
                index = findMatch(onlineIndices, merged, candidate);
            }
            if (index < 0 && metadataKey is not null &&
                byMetadataKey.TryGetValue(metadataKey, out var metadataIndices))
            {
                index = findMatch(metadataIndices, merged, candidate);
            }

            if (index < 0)
            {
                merged.Add(candidate);
                index = merged.Count - 1;
            }
            else
            {
                merged[index] = merge(merged[index], candidate);
            }

            var result = merged[index];
            if (result.OnlineId is > 0)
            {
                if (!byOnlineId.TryGetValue(result.OnlineId.Value, out var indices))
                    byOnlineId[result.OnlineId.Value] = indices = [];
                if (!indices.Contains(index)) indices.Add(index);
            }

            if (metadataKey is not null)
            {
                if (!byMetadataKey.TryGetValue(metadataKey, out var indices))
                    byMetadataKey[metadataKey] = indices = [];
                if (!indices.Contains(index)) indices.Add(index);
            }
        }

        return merged;
    }

    private int findMatch(IEnumerable<int> indices, IReadOnlyList<UnifiedBeatmapSet> sets, UnifiedBeatmapSet candidate)
    {
        foreach (var index in indices)
        {
            if (duplicateDetector.AreDuplicates(sets[index], candidate)) return index;
        }
        return -1;
    }

    private async Task<IReadOnlyList<UnifiedBeatmapSet>> loadInstallationAsync(
        OsuInstallation installation,
        CancellationToken cancellationToken)
    {
        var loader = loaders.FirstOrDefault(candidate => candidate.Kind == installation.Kind);
        return loader is null
            ? []
            : await loader.LoadAsync(installation.RootPath, cancellationToken).ConfigureAwait(false);
    }

    private static UnifiedBeatmapSet merge(UnifiedBeatmapSet first, UnifiedBeatmapSet second)
    {
        var beatmaps = first.Beatmaps
            .Concat(second.Beatmaps)
            .GroupBy(static beatmap => beatmap.OnlineId is > 0 ? $"online:{beatmap.OnlineId}" : $"local:{beatmap.DifficultyName}", StringComparer.OrdinalIgnoreCase)
            .Select(static group => mergeDifficulty(group.ToArray()))
            .ToArray();

        return first with
        {
            OnlineId = first.OnlineId is > 0 ? first.OnlineId : second.OnlineId,
            TitleUnicode = prefer(first.TitleUnicode, second.TitleUnicode),
            ArtistUnicode = prefer(first.ArtistUnicode, second.ArtistUnicode),
            AudioFilePath = preferExistingFile(first.AudioFilePath, second.AudioFilePath),
            BackgroundFilePath = preferExistingFile(first.BackgroundFilePath, second.BackgroundFilePath),
            Source = first.Source == second.Source ? first.Source : BeatmapSource.Both,
            Files = first.Files ?? second.Files,
            Beatmaps = beatmaps,
        };
    }

    /// <summary>
    /// The first difficulty of a group wins, but every hash the others carried is kept so
    /// collections made in either installation still find it.
    /// </summary>
    private static UnifiedBeatmap mergeDifficulty(UnifiedBeatmap[] group)
    {
        var winner = group[0];
        if (group.Length == 1)
        {
            return winner;
        }

        var alternates = group
            .SelectMany(static beatmap => beatmap.AllMd5Hashes)
            .Where(hash => !string.Equals(hash, winner.Md5Hash, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return alternates.Length == 0 ? winner : winner with { AlternateMd5Hashes = alternates };
    }

    private static string prefer(string first, string second) => string.IsNullOrWhiteSpace(first) ? second : first;

    private static string? preferExistingFile(string? first, string? second) =>
        first is not null && File.Exists(first) ? first : second ?? first;
}
