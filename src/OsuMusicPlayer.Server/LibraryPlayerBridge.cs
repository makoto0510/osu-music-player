using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.Server;

/// <summary>
/// A bridge for headless hosts (a Raspberry Pi serving a lazer library): it exposes the
/// library and streams audio, but has no local playback, so transport commands are no-ops
/// and clients play through their own browser.
/// </summary>
public sealed class LibraryPlayerBridge : IPlayerBridge
{
    private readonly IReadOnlyList<ServerTrack> tracks;
    private readonly Dictionary<Guid, ServerTrack> byId;
    private readonly HashSet<Guid> favourites = [];

    public LibraryPlayerBridge(IEnumerable<UnifiedBeatmapSet> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);
        tracks = sets.Where(static set => !string.IsNullOrWhiteSpace(set.AudioFilePath)).Select(toTrack).ToArray();
        byId = tracks.ToDictionary(static track => track.Id);
    }

    public int TrackCount => tracks.Count;

    public Task<IReadOnlyList<ServerTrack>> GetTracksAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ServerTrack>>(tracks.Select(withFavourite).ToArray());

    public Task<ServerTrack?> GetTrackAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(byId.TryGetValue(id, out var track) ? withFavourite(track) : null);

    public Task<PlayerState> GetStateAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new PlayerState(null, false, 0, 0, 1, "None", false, "Off", []));

    public Task<bool> PlayAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task TogglePlayAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task NextAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task PreviousAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SeekAsync(double seconds, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetVolumeAsync(double volume, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> EnqueueAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<bool> ToggleFavouriteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!byId.ContainsKey(id))
        {
            return Task.FromResult(false);
        }

        if (!favourites.Remove(id))
        {
            favourites.Add(id);
        }

        return Task.FromResult(true);
    }

    private ServerTrack withFavourite(ServerTrack track) => favourites.Contains(track.Id) ? track with { IsFavourite = true } : track;

    private static ServerTrack toTrack(UnifiedBeatmapSet set) => new(
        set.Id,
        string.IsNullOrWhiteSpace(set.TitleUnicode) ? set.Title : set.TitleUnicode,
        string.IsNullOrWhiteSpace(set.ArtistUnicode) ? set.Artist : set.ArtistUnicode,
        set.Creator,
        set.Beatmaps.Count == 0 ? 0 : set.Beatmaps.Max(static beatmap => beatmap.BPM),
        set.Beatmaps.Count == 0 ? 0 : set.Beatmaps.Max(static beatmap => beatmap.Length).TotalSeconds,
        set.OnlineId,
        false,
        set.Source switch
        {
            BeatmapSource.Stable => "stable",
            BeatmapSource.Lazer => "lazer",
            _ => "both",
        },
        set.BackgroundFilePath is not null)
    {
        TitleRomanised = set.Title,
        ArtistRomanised = set.Artist,
        AudioPath = set.AudioFilePath,
        BackgroundPath = set.BackgroundFilePath,
    };
}
