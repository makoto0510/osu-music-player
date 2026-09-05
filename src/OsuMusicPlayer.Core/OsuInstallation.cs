namespace OsuMusicPlayer.Core;

public enum OsuInstallationKind
{
    Stable,
    Lazer,
}

public sealed record OsuInstallation(OsuInstallationKind Kind, string RootPath);
