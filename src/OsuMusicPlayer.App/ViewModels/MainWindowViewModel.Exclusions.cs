using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OsuMusicPlayer.App.Services;
using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Core.Search;

namespace OsuMusicPlayer.App.ViewModels;

/// <summary>
/// Library exclusions: sets that are too short or too long, or that match an exclusion
/// query, are dropped when the library is built. Nothing in the osu! folders changes.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private IReadOnlyList<UnifiedBeatmapSet> loadedModels = [];
    private int lastExcludedCount;

    [ObservableProperty]
    private int excludeMinLengthSeconds;

    [ObservableProperty]
    private int excludeMaxLengthSeconds;

    [ObservableProperty]
    private string excludeQueryText = string.Empty;

    [ObservableProperty]
    private string exclusionStatusText = string.Empty;

    /// <summary>Re-filters the already loaded library with the current exclusion settings.</summary>
    [RelayCommand]
    private void ApplyExclusions()
    {
        if (loadedModels.Count == 0)
        {
            return;
        }

        var previousQuery = SearchText;
        replaceTracks(buildTrackItems(loadedModels));
        LibraryStatusText = $"{allTracks.Count:N0} tracks ({lastExcludedCount:N0} excluded)";
        SearchText = previousQuery;
        RequestSettingsSave();
    }

    private TrackItemViewModel[] buildTrackItems(IReadOnlyList<UnifiedBeatmapSet> models)
    {
        var minimum = ExcludeMinLengthSeconds > 0 ? TimeSpan.FromSeconds(ExcludeMinLengthSeconds) : (TimeSpan?)null;
        var maximum = ExcludeMaxLengthSeconds > 0 ? TimeSpan.FromSeconds(ExcludeMaxLengthSeconds) : (TimeSpan?)null;
        var exclusion = TrackSearchQuery.Parse(ExcludeQueryText);
        var kept = new List<TrackItemViewModel>(models.Count);
        var excluded = 0;
        foreach (var model in models)
        {
            if (string.IsNullOrWhiteSpace(model.AudioFilePath))
            {
                continue;
            }

            var length = model.Beatmaps.Count == 0 ? TimeSpan.Zero : model.Beatmaps.Max(static beatmap => beatmap.Length);
            if ((minimum is { } min && length < min) ||
                (maximum is { } max && length > max) ||
                (!exclusion.IsEmpty && exclusion.Matches(model)))
            {
                excluded++;
                continue;
            }

            kept.Add(new TrackItemViewModel(model, imageLoader));
        }

        lastExcludedCount = excluded;
        ExclusionStatusText = excluded == 0 ? "No tracks are excluded." : $"{excluded:N0} tracks are hidden by these rules.";
        return kept.ToArray();
    }

    private void applyRestoredExclusions(AppSettings settings)
    {
        ExcludeMinLengthSeconds = Math.Max(0, settings.Exclusions.MinimumLengthSeconds);
        ExcludeMaxLengthSeconds = Math.Max(0, settings.Exclusions.MaximumLengthSeconds);
        ExcludeQueryText = settings.Exclusions.ExcludeQuery ?? string.Empty;
    }
}
