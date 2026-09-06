namespace OsuMusicPlayer.Server;

/// <summary>A track as exposed over HTTP. File paths stay on the server side.</summary>
public sealed record ServerTrack(
    Guid Id,
    string Title,
    string Artist,
    string Creator,
    double Bpm,
    double LengthSeconds,
    long? OnlineId,
    bool IsFavourite,
    string Source,
    bool HasBackground)
{
    /// <summary>Romanised title and artist (osu!'s ASCII metadata) for searching and display.</summary>
    public string? TitleRomanised { get; init; }

    public string? ArtistRomanised { get; init; }

    /// <summary>Server-side only: where the audio lives; never serialized.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? AudioPath { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string? BackgroundPath { get; init; }
}

public sealed record PlayerState(
    ServerTrack? Current,
    bool IsPlaying,
    double PositionSeconds,
    double DurationSeconds,
    double Volume,
    string Mod,
    bool Shuffle,
    string Repeat,
    IReadOnlyList<ServerTrack> Queue);

/// <summary>
/// What the server needs from the player. Implementations marshal to the UI thread; the
/// server never touches view models directly.
/// </summary>
public interface IPlayerBridge
{
    Task<IReadOnlyList<ServerTrack>> GetTracksAsync(CancellationToken cancellationToken);

    Task<ServerTrack?> GetTrackAsync(Guid id, CancellationToken cancellationToken);

    Task<PlayerState> GetStateAsync(CancellationToken cancellationToken);

    Task<bool> PlayAsync(Guid id, CancellationToken cancellationToken);

    Task TogglePlayAsync(CancellationToken cancellationToken);

    Task PauseAsync(CancellationToken cancellationToken);

    Task ResumeAsync(CancellationToken cancellationToken);

    Task NextAsync(CancellationToken cancellationToken);

    Task PreviousAsync(CancellationToken cancellationToken);

    Task SeekAsync(double seconds, CancellationToken cancellationToken);

    Task SetVolumeAsync(double volume, CancellationToken cancellationToken);

    Task<bool> EnqueueAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ToggleFavouriteAsync(Guid id, CancellationToken cancellationToken);
}
