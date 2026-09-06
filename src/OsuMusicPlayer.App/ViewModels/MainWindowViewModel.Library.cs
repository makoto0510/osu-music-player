using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OsuMusicPlayer.App.Services;
using OsuMusicPlayer.Core;
using OsuMusicPlayer.Core.Loaders;
using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Core.Search;

namespace OsuMusicPlayer.App.ViewModels;

/// <summary>Library views (favourites, playlists, collections), browsing chips and web links.</summary>
public sealed partial class MainWindowViewModel
{
    private const int browse_chip_count = 40;

    private readonly HashSet<Guid> favouriteIds = [];
    private readonly Dictionary<Guid, TrackItemViewModel> tracksById = [];
    private readonly Dictionary<string, TrackItemViewModel> tracksByMd5 = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<BeatmapCollectionInfo> collections = [];
    private TrackSearchQuery searchQuery = TrackSearchQuery.Empty;
    private bool suppressViewRefresh;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaylistViewSelected))]
    private LibraryView? selectedView;

    [ObservableProperty]
    private PlaylistViewModel? selectedPlaylist;

    [ObservableProperty]
    private string newPlaylistName = string.Empty;

    [ObservableProperty]
    private bool isPlaylistPanelVisible;

    [ObservableProperty]
    private bool isBrowsePanelVisible;

    public ObservableCollection<LibraryView> Views { get; } = [];
    public ObservableCollection<PlaylistViewModel> Playlists { get; } = [];
    public ObservableCollection<SmartPlaylistSetting> SmartPlaylists { get; } = [];
    public ObservableCollection<BrowseEntry> TopArtists { get; } = [];
    public ObservableCollection<BrowseEntry> TopMappers { get; } = [];
    public ObservableCollection<BrowseEntry> TopTags { get; } = [];

    public bool IsPlaylistViewSelected => SelectedView?.Kind == LibraryViewKind.Playlist;
    public bool IsDetailFavourite => DetailTrack?.IsFavourite == true;
    public string FavouriteButtonText => IsDetailFavourite ? "♥ Favourited" : "♡ Favourite";
    public bool CanOpenOnWeb => DetailTrack?.Model.OnlineId is > 0;
    public string SearchHelpText => "Search: words, artist: title: mapper: tag: mode: bpm:120-180 stars:>5 length:<3:00 -exclude a|b";

    [RelayCommand]
    private void ToggleFavourite()
    {
        if (DetailTrack is not { } track)
        {
            return;
        }

        track.IsFavourite = !track.IsFavourite;
        if (track.IsFavourite)
        {
            favouriteIds.Add(track.Model.Id);
        }
        else
        {
            favouriteIds.Remove(track.Model.Id);
        }

        notifyFavouriteChanged();
        rebuildViews();
        if (SelectedView?.Kind == LibraryViewKind.Favourites)
        {
            applyFilterAndSort();
        }

        RequestSettingsSave();
    }

    [RelayCommand]
    private void CreatePlaylist()
    {
        var name = NewPlaylistName.Trim();
        if (name.Length == 0)
        {
            name = $"Playlist {Playlists.Count + 1}";
        }

        var playlist = new PlaylistViewModel(Guid.NewGuid(), name);
        Playlists.Add(playlist);
        SelectedPlaylist = playlist;
        NewPlaylistName = string.Empty;
        rebuildViews();
        SelectedView = Views.FirstOrDefault(view => view.Kind == LibraryViewKind.Playlist && view.Id == playlist.Id);
        RequestSettingsSave();
    }

    [RelayCommand]
    private void RenamePlaylist()
    {
        var name = NewPlaylistName.Trim();
        if (SelectedPlaylist is not { } playlist || name.Length == 0)
        {
            return;
        }

        playlist.Name = name;
        NewPlaylistName = string.Empty;
        rebuildViews();
        RequestSettingsSave();
    }

    [RelayCommand]
    private void DeletePlaylist()
    {
        if (SelectedPlaylist is not { } playlist)
        {
            return;
        }

        var wasViewing = SelectedView?.Kind == LibraryViewKind.Playlist && SelectedView.Id == playlist.Id;
        Playlists.Remove(playlist);
        SelectedPlaylist = Playlists.FirstOrDefault();
        rebuildViews();
        if (wasViewing)
        {
            SelectedView = Views.FirstOrDefault();
        }

        RequestSettingsSave();
    }

    [RelayCommand]
    private void AddToPlaylist()
    {
        if (SelectedPlaylist is not { } playlist || DetailTrack is not { } track)
        {
            return;
        }

        if (!playlist.Add(track.Model.Id))
        {
            LibraryStatusText = $"既に「{playlist.Name}」に入っています: {track.Title}";
            return;
        }

        LibraryStatusText = $"「{playlist.Name}」に追加しました: {track.Title}";
        rebuildViews();
        refreshIfViewing(playlist);
        RequestSettingsSave();
    }

    [RelayCommand]
    private void RemoveFromPlaylist(TrackItemViewModel? track)
    {
        track ??= SelectedTrack;
        if (track is null || currentViewPlaylist() is not { } playlist || !playlist.Remove(track.Model.Id))
        {
            return;
        }

        rebuildViews();
        refreshIfViewing(playlist);
        RequestSettingsSave();
    }

    [RelayCommand]
    private void MoveTrackUp() => moveInPlaylist(-1);

    [RelayCommand]
    private void MoveTrackDown() => moveInPlaylist(1);

    [RelayCommand]
    private void SelectBrowse(BrowseEntry? entry)
    {
        if (entry is not null)
        {
            SearchText = entry.Query;
        }
    }

    [RelayCommand]
    private void OpenOnWeb()
    {
        if (DetailTrack?.Model.OnlineId is not { } id || id <= 0)
        {
            return;
        }

        if (!linkOpener.Open(new Uri($"https://osu.ppy.sh/beatmapsets/{id}")))
        {
            ErrorMessage = "ブラウザーを開けませんでした。";
        }
    }

    partial void OnSelectedViewChanged(LibraryView? value)
    {
        OnPropertyChanged(nameof(IsSmartPlaylistViewSelected));
        if (!suppressViewRefresh)
        {
            applyFilterAndSort();
        }
    }

    private PlaylistViewModel? currentViewPlaylist() =>
        SelectedView?.Kind == LibraryViewKind.Playlist ? Playlists.FirstOrDefault(playlist => playlist.Id == SelectedView.Id) : null;

    private void moveInPlaylist(int offset)
    {
        if (SelectedTrack is not { } track || currentViewPlaylist() is not { } playlist || !playlist.Move(track.Model.Id, offset))
        {
            return;
        }

        applyFilterAndSort();
        SelectedTrack = track;
        RequestSettingsSave();
    }

    private void refreshIfViewing(PlaylistViewModel playlist)
    {
        if (SelectedView?.Kind == LibraryViewKind.Playlist && SelectedView.Id == playlist.Id)
        {
            applyFilterAndSort();
        }
    }

    private void notifyFavouriteChanged()
    {
        OnPropertyChanged(nameof(IsDetailFavourite));
        OnPropertyChanged(nameof(FavouriteButtonText));
    }

    /// <summary>Applies the selected view on top of the full track list.</summary>
    private IEnumerable<TrackItemViewModel> filterByView(IEnumerable<TrackItemViewModel> tracks) =>
        SelectedView is null ? tracks : SelectedView.Filter(tracks);

    private void rebuildLibraryIndexes()
    {
        tracksById.Clear();
        tracksByMd5.Clear();
        foreach (var track in allTracks)
        {
            tracksById[track.Model.Id] = track;
            track.IsFavourite = favouriteIds.Contains(track.Model.Id);
            foreach (var beatmap in track.Model.Beatmaps)
            {
                if (beatmap.Md5Hash is { Length: > 0 } hash)
                {
                    tracksByMd5.TryAdd(hash, track);
                }
            }
        }

        applyCachedOnlineMetadata();
        rebuildViews();
        rebuildBrowse();
        notifyFavouriteChanged();
    }

    private void rebuildViews()
    {
        var previous = SelectedView;
        suppressViewRefresh = true;
        try
        {
            Views.Clear();
            Views.Add(new LibraryView(LibraryViewKind.All, "All tracks", null, static tracks => tracks, allTracks.Count));
            Views.Add(new LibraryView(LibraryViewKind.Favourites, "Favourites", null, tracks => tracks.Where(static track => track.IsFavourite), allTracks.Count(static track => track.IsFavourite)));
            if (recommendationsAvailable)
            {
                Views.Add(new LibraryView(LibraryViewKind.Recommended, "Recommended", null, recommendTracks, Math.Min(recommendation_count, allTracks.Count)));
            }

            foreach (var playlist in Playlists)
            {
                var ids = playlist.TrackIds;
                Views.Add(new LibraryView(LibraryViewKind.Playlist, playlist.Name, playlist.Id,
                    _ => ids.Select(id => tracksById.GetValueOrDefault(id)).Where(static track => track is not null)!,
                    ids.Count(id => tracksById.ContainsKey(id))));
            }

            foreach (var smart in SmartPlaylists)
            {
                var query = TrackSearchQuery.Parse(smart.Query);
                Views.Add(new LibraryView(LibraryViewKind.SmartPlaylist, smart.Name, smart.Id,
                    tracks => tracks.Where(track => query.Matches(track.Model, track.Genre, track.Language)),
                    allTracks.Count(track => query.Matches(track.Model, track.Genre, track.Language))));
            }

            foreach (var group in collections.GroupBy(static collection => collection.Name, StringComparer.OrdinalIgnoreCase))
            {
                var ambiguous = group.Count() > 1;
                foreach (var collection in group)
                {
                    var name = ambiguous ? $"{collection.Name} ({(collection.Source == OsuInstallationKind.Stable ? "stable" : "lazer")})" : collection.Name;
                    var members = collection.BeatmapMd5Hashes.Select(hash => tracksByMd5.GetValueOrDefault(hash)).Where(static track => track is not null).Distinct().ToArray();
                    Views.Add(new LibraryView(LibraryViewKind.Collection, name, null, tracks => tracks.Where(members.Contains), members.Length));
                }
            }

            SelectedView = Views.FirstOrDefault(view => view.SameAs(previous)) ?? Views.FirstOrDefault();
        }
        finally
        {
            suppressViewRefresh = false;
        }

        OnPropertyChanged(nameof(IsPlaylistViewSelected));
    }

    private void rebuildBrowse()
    {
        fill(TopArtists, allTracks.GroupBy(static track => track.Artist, StringComparer.CurrentCultureIgnoreCase), static name => $"artist:\"{name}\"");
        fill(TopMappers, allTracks.GroupBy(static track => track.Creator, StringComparer.CurrentCultureIgnoreCase), static name => $"mapper:\"{name}\"");
        var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var track in allTracks)
        {
            foreach (var tag in track.Tags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (tag.Length > 1)
                {
                    tagCounts[tag] = tagCounts.GetValueOrDefault(tag) + 1;
                }
            }
        }

        TopTags.Clear();
        foreach (var (tag, count) in tagCounts.OrderByDescending(static pair => pair.Value).ThenBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase).Take(browse_chip_count))
        {
            TopTags.Add(new BrowseEntry(tag, $"tag:\"{tag}\"", count));
        }

        static void fill(ObservableCollection<BrowseEntry> target, IEnumerable<IGrouping<string, TrackItemViewModel>> groups, Func<string, string> query)
        {
            target.Clear();
            foreach (var group in groups.Where(static group => !string.IsNullOrWhiteSpace(group.Key)).OrderByDescending(static group => group.Count()).ThenBy(static group => group.Key, StringComparer.CurrentCultureIgnoreCase).Take(browse_chip_count))
            {
                target.Add(new BrowseEntry(group.Key, query(group.Key), group.Count()));
            }
        }
    }

    private async Task<IReadOnlyList<BeatmapCollectionInfo>> loadCollectionsAsync(IReadOnlyList<OsuInstallation> installations, CancellationToken cancellationToken)
    {
        var result = new List<BeatmapCollectionInfo>();
        foreach (var installation in installations)
        {
            var loader = collectionLoaders.FirstOrDefault(candidate => candidate.Kind == installation.Kind);
            if (loader is null)
            {
                continue;
            }

            try
            {
                result.AddRange(await loader.LoadAsync(installation.RootPath, cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // Collections are optional; the library still loads without them.
            }
        }

        return result;
    }

    private void applyRestoredLibrary(AppSettings settings)
    {
        favouriteIds.Clear();
        favouriteIds.UnionWith(settings.Favourites);
        Playlists.Clear();
        foreach (var playlist in settings.Playlists)
        {
            Playlists.Add(new PlaylistViewModel(playlist.Id, playlist.Name, playlist.TrackIds));
        }

        SmartPlaylists.Clear();
        foreach (var smart in settings.SmartPlaylists)
        {
            SmartPlaylists.Add(smart);
        }

        SelectedPlaylist = Playlists.FirstOrDefault();
    }
}
