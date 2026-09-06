using OsuMusicPlayer.App.ViewModels;
using OsuMusicPlayer.Audio;
using OsuMusicPlayer.Core;

namespace OsuMusicPlayer.App.Services;

public sealed record ManualInstallationSetting(OsuInstallationKind Kind, string Path);

public sealed record PlaylistSetting(Guid Id, string Name, IReadOnlyList<Guid> TrackIds);

/// <summary>A saved search; the player's query language does the filtering.</summary>
public sealed record SmartPlaylistSetting(Guid Id, string Name, string Query);

public sealed record PlaybackStateSetting(Guid TrackId, double PositionSeconds, IReadOnlyList<Guid> QueueIds);

public sealed record PlayHistoryEntry(Guid TrackId, int PlayCount, DateTime LastPlayedUtc);

/// <summary>Rules that hide sets from the library without touching any osu! files.</summary>
public sealed class LibraryExclusionSettings
{
    /// <summary>Sets shorter than this are hidden; 0 disables the rule.</summary>
    public int MinimumLengthSeconds { get; init; }

    /// <summary>Sets longer than this are hidden; 0 disables the rule.</summary>
    public int MaximumLengthSeconds { get; init; }

    /// <summary>A search-language query; sets that match are hidden (e.g. <c>mode:mania</c> or <c>tag:tv</c>).</summary>
    public string ExcludeQuery { get; init; } = string.Empty;
}

public sealed class ServerSettings
{
    public bool Enabled { get; init; }

    public int Port { get; init; } = 5150;

    /// <summary>Listen on every interface so phones on the LAN can connect; off means localhost only.</summary>
    public bool AllowRemoteConnections { get; init; } = true;
}

public sealed class AppearanceSettings
{
    public string ThemeName { get; init; } = "osu! Pink";

    /// <summary>Hex accent override such as "#FF66AA"; empty keeps the preset's accent.</summary>
    public string AccentColor { get; init; } = string.Empty;
}

public sealed class AppSettings
{
    public IReadOnlyList<ManualInstallationSetting> ManualInstallations { get; init; } = [];

    public IReadOnlyList<PlaylistSetting> Playlists { get; init; } = [];

    public IReadOnlyList<SmartPlaylistSetting> SmartPlaylists { get; init; } = [];

    public IReadOnlyList<Guid> Favourites { get; init; } = [];

    public double Volume { get; init; } = 1;

    public OsuAudioMod Mod { get; init; }

    public bool Shuffle { get; init; }

    public RepeatMode Repeat { get; init; }

    public TrackSortOption Sort { get; init; } = TrackSortOption.Title;

    public bool HitsoundsEnabled { get; init; }

    public double HitsoundVolume { get; init; } = 1;

    public int HitsoundOffsetMs { get; init; }

    public bool StoryboardEnabled { get; init; } = true;

    public bool VideoEnabled { get; init; } = true;

    /// <summary>Equalizer gains in dB for the ten bands; empty means flat.</summary>
    public IReadOnlyList<float> EqualizerGains { get; init; } = [];

    public PlaybackStateSetting? LastPlayback { get; init; }

    public IReadOnlyList<PlayHistoryEntry> PlayHistory { get; init; } = [];

    public ServerSettings Server { get; init; } = new();

    public LibraryExclusionSettings Exclusions { get; init; } = new();

    public bool RichPresenceEnabled { get; init; }

    /// <summary>Discord application id created by the user at discord.com/developers; empty disables presence.</summary>
    public string DiscordApplicationId { get; init; } = string.Empty;

    public string OsuApiClientId { get; init; } = string.Empty;

    public string OsuApiClientSecret { get; init; } = string.Empty;

    public AppearanceSettings Appearance { get; init; } = new();
}
