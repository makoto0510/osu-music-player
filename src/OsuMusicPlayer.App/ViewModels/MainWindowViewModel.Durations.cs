using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.App.ViewModels;

/// <summary>Fills in the length of sets whose database entry says 0 ms by probing the audio file.</summary>
public sealed partial class MainWindowViewModel
{
    private const int max_duration_probes_per_load = 300;

    private async Task<IReadOnlyList<UnifiedBeatmapSet>> fillMissingDurationsAsync(IReadOnlyList<UnifiedBeatmapSet> models, CancellationToken cancellationToken)
    {
        if (durationProbe is null)
        {
            return models;
        }

        var targets = models
            .Select((set, index) => (Set: set, Index: index))
            .Where(static pair => pair.Set.AudioFilePath is not null && pair.Set.Beatmaps.Count > 0 && pair.Set.Beatmaps.All(static beatmap => beatmap.Length <= TimeSpan.Zero))
            .Take(max_duration_probes_per_load)
            .ToArray();
        if (targets.Length == 0)
        {
            return models;
        }

        var result = models.ToArray();
        await Task.Run(() =>
        {
            foreach (var (set, index) in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (durationProbe.Probe(set.AudioFilePath!) is not { } duration)
                {
                    continue;
                }

                result[index] = set with
                {
                    Beatmaps = set.Beatmaps.Select(beatmap => beatmap with { Length = duration }).ToArray(),
                };
            }
        }, cancellationToken).ConfigureAwait(false);

        return result;
    }
}
