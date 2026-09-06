namespace OsuMusicPlayer.Core.Models;

/// <summary>An in-game collection: a named list of beatmap MD5 hashes.</summary>
public sealed record BeatmapCollectionInfo(string Name, OsuInstallationKind Source, IReadOnlyList<string> BeatmapMd5Hashes);
