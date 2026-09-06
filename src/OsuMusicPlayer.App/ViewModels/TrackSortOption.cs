namespace OsuMusicPlayer.App.ViewModels;

public enum TrackSortOption
{
    Title,
    Artist,
    BPM,
    Length,
    RecentlyPlayed,
    MostPlayed,
    Stars,
}

public static class TrackSortOptionExtensions
{
    /// <summary>Human label used in the list header ("Sorted by …") and the sort menu.</summary>
    public static string ToLabel(this TrackSortOption option) => option switch
    {
        TrackSortOption.Artist => "artist",
        TrackSortOption.BPM => "BPM",
        TrackSortOption.Length => "length",
        TrackSortOption.RecentlyPlayed => "recently played",
        TrackSortOption.MostPlayed => "most played",
        TrackSortOption.Stars => "star rating",
        _ => "title",
    };
}
