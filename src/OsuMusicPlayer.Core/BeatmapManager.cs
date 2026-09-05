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
        var byOnlineId = new Dictionary<long, int>();
        var byMetadataKey = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var candidate in loaded)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadataKey = duplicateDetector.CreateMetadataKey(candidate);
            var index = -1;
            if (candidate.OnlineId is > 0 && byOnlineId.TryGetValue(candidate.OnlineId.Value, out var onlineIndex))
            {
                index = onlineIndex;
            }
            else if (metadataKey is not null &&
                byMetadataKey.TryGetValue(metadataKey, out var metadataIndex) &&
                duplicateDetector.AreDuplicates(merged[metadataIndex], candidate))
            {
                index = metadataIndex;
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
                byOnlineId.TryAdd(result.OnlineId.Value, index);
            }

            if (metadataKey is not null)
            {
                byMetadataKey.TryAdd(metadataKey, index);
            }
        }

        return merged;
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
            .Select(static group => group.First())
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

    private static string prefer(string first, string second) => string.IsNullOrWhiteSpace(first) ? second : first;

    private static string? preferExistingFile(string? first, string? second) =>
        first is not null && File.Exists(first) ? first : second ?? first;
}
