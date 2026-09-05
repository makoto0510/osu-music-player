using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.Core.Loaders;

public interface IBeatmapLoader
{
    OsuInstallationKind Kind { get; }

    Task<IReadOnlyList<UnifiedBeatmapSet>> LoadAsync(string installationPath, CancellationToken cancellationToken = default);
}
