namespace OsuMusicPlayer.Mobile.Core;

public sealed record MobileTrack(
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
    public string? TitleRomanised { get; init; }

    public string? ArtistRomanised { get; init; }
}

public sealed record MobileTrackPage(int Total, int Offset, IReadOnlyList<MobileTrack> Items);

public sealed record MobilePlayerState(
    MobileTrack? Current,
    bool IsPlaying,
    double PositionSeconds,
    double DurationSeconds,
    double Volume,
    string Mod,
    bool Shuffle,
    string Repeat,
    IReadOnlyList<MobileTrack> Queue);
