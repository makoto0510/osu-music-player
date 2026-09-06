using CommunityToolkit.Mvvm.ComponentModel;

namespace OsuMusicPlayer.App.ViewModels;

public sealed partial class PlaylistViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string name;

    public PlaylistViewModel(Guid id, string name, IEnumerable<Guid>? trackIds = null)
    {
        Id = id;
        this.name = name ?? throw new ArgumentNullException(nameof(name));
        TrackIds = trackIds is null ? [] : trackIds.Distinct().ToList();
    }

    public Guid Id { get; }

    /// <summary>Ordered track ids; the same track appears at most once.</summary>
    public List<Guid> TrackIds { get; }

    public int Count => TrackIds.Count;

    public string DisplayName => $"{Name} ({Count:N0})";

    public bool Add(Guid trackId)
    {
        if (TrackIds.Contains(trackId))
        {
            return false;
        }

        TrackIds.Add(trackId);
        notifyCount();
        return true;
    }

    public bool Remove(Guid trackId)
    {
        var removed = TrackIds.Remove(trackId);
        if (removed)
        {
            notifyCount();
        }

        return removed;
    }

    public bool Move(Guid trackId, int offset)
    {
        var index = TrackIds.IndexOf(trackId);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= TrackIds.Count)
        {
            return false;
        }

        TrackIds.RemoveAt(index);
        TrackIds.Insert(target, trackId);
        return true;
    }

    public void NotifyChanged() => notifyCount();

    private void notifyCount()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(DisplayName));
    }

    public override string ToString() => DisplayName;
}
