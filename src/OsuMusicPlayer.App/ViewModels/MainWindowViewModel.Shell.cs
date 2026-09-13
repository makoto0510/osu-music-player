using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Core.Search;

namespace OsuMusicPlayer.App.ViewModels;

/// <summary>One entry of the ruleset filter in the header ("All", "osu!", …).</summary>
public sealed record ModeFilter(string Name, OsuRuleset? Ruleset)
{
    public string QueryText => Ruleset switch
    {
        OsuRuleset.Osu => "mode:osu",
        OsuRuleset.Taiko => "mode:taiko",
        OsuRuleset.Catch => "mode:catch",
        OsuRuleset.Mania => "mode:mania",
        _ => string.Empty,
    };

    public override string ToString() => Name;
}

/// <summary>
/// State that only exists for the window shell: the grouped sidebar, the list header texts,
/// the ruleset filter and the per-row commands of the track table.
/// </summary>
public sealed partial class MainWindowViewModel
{
    public static IReadOnlyList<ModeFilter> ModeFilters { get; } =
    [
        new("All", null),
        new("osu!", OsuRuleset.Osu),
        new("taiko", OsuRuleset.Taiko),
        new("catch", OsuRuleset.Catch),
        new("mania", OsuRuleset.Mania),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeFilterText))]
    [NotifyPropertyChangedFor(nameof(IsModeFilterActive))]
    private ModeFilter selectedModeFilter = ModeFilters[0];

    /// <summary>The now-playing / queue pane on the right (Ctrl+Q hides it to widen the list).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsColumnWidth))]
    private bool isNowPlayingPaneVisible = true;

    /// <summary>The "Library" group of the sidebar: all tracks, favourites, recommendations.</summary>
    public ObservableCollection<LibraryView> LibraryViews { get; } = [];

    /// <summary>Playlists and smart playlists.</summary>
    public ObservableCollection<LibraryView> PlaylistViews { get; } = [];

    /// <summary>osu! collections read from stable / lazer.</summary>
    public ObservableCollection<LibraryView> CollectionViews { get; } = [];

    public bool HasPlaylistViews => PlaylistViews.Count > 0;
    public bool HasCollectionViews => CollectionViews.Count > 0;

    public string ModeFilterText => SelectedModeFilter.Name;
    public bool IsModeFilterActive => SelectedModeFilter.Ruleset is not null;

    /// <summary>Big title above the track table.</summary>
    public string ViewTitle => SelectedView switch
    {
        null or { Kind: LibraryViewKind.All } => "All Songs",
        { Kind: LibraryViewKind.Favourites } => "Favourites",
        { Kind: LibraryViewKind.Recommended } => "Recommended for you",
        { } view => view.Name,
    };

    public string TrackCountText => Tracks.Count == 1 ? "1 track" : $"{Tracks.Count:N0} tracks";

    public string SortLabelText => SelectedView?.KeepsOwnOrder == true ? "Playlist order" : $"Sorted by {SelectedSort.ToLabel()}";

    public bool IsSortRecentlyPlayed => SelectedSort == TrackSortOption.RecentlyPlayed;
    public bool IsSortMostPlayed => SelectedSort == TrackSortOption.MostPlayed;
    public bool IsSortStars => SelectedSort == TrackSortOption.Stars;

    /// <summary>The right pane shows the playing track unless another one is selected.</summary>
    public bool IsDetailCurrent => DetailTrack is not null && DetailTrack == CurrentTrack;

    public string NowPlayingLabel => IsDetailCurrent ? (IsPlaying ? "NOW PLAYING" : "PAUSED") : "SELECTED";

    public string DetailFavouriteIconKey => IsDetailFavourite ? "IconHeart" : "IconHeartOutline";

    public bool IsRepeatOn => RepeatMode != RepeatMode.Off;

    public bool IsRepeatOne => RepeatMode == RepeatMode.One;

    public bool IsMuted => MasterVolume <= 0;

    public string VideoToggleTip => videoPlayer.IsAvailable ? "Video" : (videoPlayer.UnavailableReason ?? "Video is unavailable");

    public string ModText => Mod == Audio.OsuAudioMod.None ? "NM" : Mod.ToString();

    public bool IsNoModActive => Mod == Audio.OsuAudioMod.None;

    public string ModSummary => Mod switch
    {
        Audio.OsuAudioMod.DT => "1.50× speed · Original pitch",
        Audio.OsuAudioMod.NC => "1.50× speed · Higher pitch",
        Audio.OsuAudioMod.HT => "0.75× speed · Original pitch",
        Audio.OsuAudioMod.DC => "0.75× speed · Lower pitch",
        _ => "1.00× speed · Original pitch",
    };

    public IReadOnlyList<Audio.OsuAudioMod> ModOptions { get; } = Enum.GetValues<Audio.OsuAudioMod>();

    [RelayCommand]
    private void SelectView(LibraryView? view)
    {
        if (view is not null)
        {
            if (isLibraryNavigationView(view) && isQuickSortSelected)
            {
                SelectedSort = TrackSortOption.Title;
            }

            SelectedView = view;
        }
    }

    [RelayCommand]
    private void SetSort(TrackSortOption option) => SelectedSort = option;

    [RelayCommand]
    private void SetModeFilter(OsuRuleset? ruleset) =>
        SelectedModeFilter = ModeFilters.FirstOrDefault(filter => filter.Ruleset == ruleset) ?? ModeFilters[0];

    [RelayCommand]
    private void SearchTag(string? tag)
    {
        var normalized = tag?.Trim().TrimStart('#');
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            SearchText = $"tag:\"{normalized.Replace("\"", "\\\"")}\"";
        }
    }

    [RelayCommand]
    private void CloseSettings() => IsSettingsPanelVisible = false;

    /// <summary>Row buttons (heart, "…") swallow the click, so they select their row explicitly.</summary>
    [RelayCommand]
    private void SelectTrack(TrackItemViewModel? track)
    {
        if (track is not null)
        {
            SelectedTrack = track;
        }
    }

    [RelayCommand]
    private Task PlayTrackAsync(TrackItemViewModel? track)
    {
        if (track is null)
        {
            return Task.CompletedTask;
        }

        SelectedTrack = track;
        return playTrackAsync(track);
    }

    [RelayCommand]
    private void ToggleFavouriteFor(TrackItemViewModel? track)
    {
        if (track is null)
        {
            return;
        }

        SelectedTrack = track;
        ToggleFavouriteCommand.Execute(null);
    }

    [RelayCommand]
    private void EnqueueTrack(TrackItemViewModel? track)
    {
        if (track is not null && allTracks.Contains(track))
        {
            Queue.Add(track);
            notifyQueueChanged();
        }
    }

    /// <summary>"Add to playlist ▸ name" from the row menu; acts on the selected (or playing) track.</summary>
    [RelayCommand]
    private void AddSelectedToPlaylist(PlaylistViewModel? playlist)
    {
        if (playlist is null)
        {
            return;
        }

        SelectedPlaylist = playlist;
        AddToPlaylistCommand.Execute(null);
    }

    /// <summary>Plays a queued track right away and drops it (and everything before it) from the queue.</summary>
    [RelayCommand]
    private Task PlayQueuedAsync(TrackItemViewModel? track)
    {
        if (track is null)
        {
            return Task.CompletedTask;
        }

        var index = Queue.IndexOf(track);
        if (index >= 0)
        {
            for (var i = 0; i <= index; i++)
            {
                Queue.RemoveAt(0);
            }

            notifyQueueChanged();
        }

        return playTrackAsync(track);
    }

    partial void OnSelectedModeFilterChanged(ModeFilter value) => refreshSearchQuery();

    /// <summary>The search text and the ruleset filter together form the query applied to the list.</summary>
    private void refreshSearchQuery()
    {
        var text = SelectedModeFilter.Ruleset is null ? SearchText : $"{SearchText} {SelectedModeFilter.QueryText}";
        searchQuery = TrackSearchQuery.Parse(text);
        applyFilterAndSort();
    }

    private void notifyShellTexts()
    {
        OnPropertyChanged(nameof(ViewTitle));
        OnPropertyChanged(nameof(SortLabelText));
        OnPropertyChanged(nameof(IsSortRecentlyPlayed));
        OnPropertyChanged(nameof(IsSortMostPlayed));
        OnPropertyChanged(nameof(IsSortStars));
    }

    private void notifyDetailCurrentChanged()
    {
        OnPropertyChanged(nameof(IsDetailCurrent));
        OnPropertyChanged(nameof(NowPlayingLabel));
    }

    /// <summary>Splits <see cref="Views"/> into the sidebar groups and mirrors the selection onto the entries.</summary>
    private void rebuildViewGroups()
    {
        fill(LibraryViews, static view => view.Kind is LibraryViewKind.All or LibraryViewKind.Favourites or LibraryViewKind.Recommended);
        fill(PlaylistViews, static view => view.Kind is LibraryViewKind.Playlist or LibraryViewKind.SmartPlaylist);
        fill(CollectionViews, static view => view.Kind == LibraryViewKind.Collection);
        OnPropertyChanged(nameof(HasPlaylistViews));
        OnPropertyChanged(nameof(HasCollectionViews));
        syncViewSelection();

        void fill(ObservableCollection<LibraryView> target, Func<LibraryView, bool> predicate)
        {
            target.Clear();
            foreach (var view in Views.Where(predicate))
            {
                target.Add(view);
            }
        }
    }

    private bool isQuickSortSelected => IsSortRecentlyPlayed || IsSortMostPlayed || IsSortStars;

    private static bool isLibraryNavigationView(LibraryView view) =>
        view.Kind is LibraryViewKind.All or LibraryViewKind.Favourites or LibraryViewKind.Recommended;

    private void syncViewSelection()
    {
        foreach (var view in Views)
        {
            view.IsSelected = view.SameAs(SelectedView)
                && !(isLibraryNavigationView(view) && isQuickSortSelected);
        }
    }
}
