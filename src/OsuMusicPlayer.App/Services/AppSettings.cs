using OsuMusicPlayer.Core;

namespace OsuMusicPlayer.App.Services;

public sealed record ManualInstallationSetting(OsuInstallationKind Kind, string Path);

public sealed class AppSettings
{
    public IReadOnlyList<ManualInstallationSetting> ManualInstallations { get; init; } = [];
}
