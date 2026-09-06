namespace OsuMusicPlayer.App.ViewModels;

public enum LibraryViewKind
{
    All,
    Favourites,
    Playlist,
    SmartPlaylist,
    Collection,
    Recommended,
}

/// <summary>One entry of the library selector: all tracks, favourites, a playlist, a collection…</summary>
public sealed class LibraryView
{
    public LibraryView(LibraryViewKind kind, string name, Guid? id, Func<IEnumerable<TrackItemViewModel>, IEnumerable<TrackItemViewModel>> filter, int count)
    {
        Kind = kind;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Id = id;
        Filter = filter ?? throw new ArgumentNullException(nameof(filter));
        Count = count;
    }

    public LibraryViewKind Kind { get; }

    public string Name { get; }

    public Guid? Id { get; }

    /// <summary>Selects (and for ordered views, orders) the tracks that belong to the view.</summary>
    public Func<IEnumerable<TrackItemViewModel>, IEnumerable<TrackItemViewModel>> Filter { get; }

    public int Count { get; }

    /// <summary>Playlists keep their own order; every other view is sorted by the sort selector.</summary>
    public bool KeepsOwnOrder => Kind == LibraryViewKind.Playlist;

    public string DisplayName => $"{Name} ({Count:N0})";

    /// <summary>Identity survives renames: views with an id compare by id, the rest by kind and name.</summary>
    public bool SameAs(LibraryView? other) =>
        other is not null && other.Kind == Kind && (Id is not null ? other.Id == Id : other.Name == Name);

    public override string ToString() => DisplayName;
}
