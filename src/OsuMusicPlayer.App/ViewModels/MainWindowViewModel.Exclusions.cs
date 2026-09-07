using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OsuMusicPlayer.App.Services;
using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Core.Search;

namespace OsuMusicPlayer.App.ViewModels;

/// <summary>
/// Library exclusions: sets that are too short or too long, or that match an exclusion
/// query, or individual tracks hidden by the user, are dropped when the library is built.
/// Nothing in the osu! folders changes.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private readonly HashSet<Guid> hiddenTrackIds = [];
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

    public int HiddenTrackCount => hiddenTrackIds.Count;

    public bool HasHiddenTracks => hiddenTrackIds.Count > 0;

    public string HiddenTracksStatusText => hiddenTrackIds.Count == 0
        ? "No tracks are individually hidden."
        : $"{hiddenTrackIds.Count:N0} track{(hiddenTrackIds.Count == 1 ? string.Empty : "s")} hidden.";

    /// <summary>Hides a track from the library. If it is currently playing, playback advances to the next track.</summary>
    [RelayCommand]
    private void HideTrack(TrackItemViewModel? track)
    {
        if (track is null)
        {
            return;
        }

        hiddenTrackIds.Add(track.Model.Id);
        var isCurrent = CurrentTrack == track;

        allTracks.Remove(track);
        Queue.Remove(track);
        notifyQueueChanged();
        shuffleHistory.Remove(track);
        applyFilterAndSort();

        if (isCurrent)
        {
            if (Tracks.Count > 0 || allTracks.Count > 0)
            {
                _ = NextCommand.ExecuteAsync(null);
            }
            else
            {
                audioEngine.Stop();
                CurrentTrack = null;
                IsPlaying = false;
            }
        }

        if (SelectedTrack == track)
        {
            SelectedTrack = Tracks.FirstOrDefault();
        }

        rebuildLibraryIndexes();
        OnPropertyChanged(nameof(HiddenTrackCount));
        OnPropertyChanged(nameof(HasHiddenTracks));
        OnPropertyChanged(nameof(HiddenTracksStatusText));
        track.Dispose();
        RequestSettingsSave();
    }

    [RelayCommand]
    private void HideTrackFor(TrackItemViewModel? track) => HideTrack(track);

    [RelayCommand]
    private void HideSelected() => HideTrack(SelectedTrack ?? CurrentTrack);

    [RelayCommand]
    private void HideCurrentTrack() => HideTrack(CurrentTrack);

    /// <summary>Unhides all individually hidden tracks and restores them to the library.</summary>
    [RelayCommand]
    private void UnhideAllTracks()
    {
        if (hiddenTrackIds.Count == 0)
        {
            return;
        }

        hiddenTrackIds.Clear();
        OnPropertyChanged(nameof(HiddenTrackCount));
        OnPropertyChanged(nameof(HasHiddenTracks));
        OnPropertyChanged(nameof(HiddenTracksStatusText));

        if (loadedModels.Count > 0)
        {
            var previousQuery = SearchText;
            replaceTracks(buildTrackItems(loadedModels));
            LibraryStatusText = $"{allTracks.Count:N0} tracks ({lastExcludedCount:N0} excluded)";
            SearchText = previousQuery;
        }

        RequestSettingsSave();
    }

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
                (!exclusion.IsEmpty && exclusion.Matches(model)) ||
                hiddenTrackIds.Contains(model.Id))
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
        hiddenTrackIds.Clear();
        hiddenTrackIds.UnionWith(settings.HiddenTracks);
        OnPropertyChanged(nameof(HiddenTrackCount));
        OnPropertyChanged(nameof(HasHiddenTracks));
        OnPropertyChanged(nameof(HiddenTracksStatusText));
        ExcludeMinLengthSeconds = Math.Max(0, settings.Exclusions.MinimumLengthSeconds);
        ExcludeMaxLengthSeconds = Math.Max(0, settings.Exclusions.MaximumLengthSeconds);
        ExcludeQueryText = settings.Exclusions.ExcludeQuery ?? string.Empty;
    }
}
