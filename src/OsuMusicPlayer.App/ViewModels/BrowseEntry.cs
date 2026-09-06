namespace OsuMusicPlayer.App.ViewModels;

/// <summary>A clickable chip in the browse panel; clicking runs <see cref="Query"/> in the search box.</summary>
public sealed record BrowseEntry(string Label, string Query, int Count)
{
    public string DisplayName => $"{Label} ({Count:N0})";
}
