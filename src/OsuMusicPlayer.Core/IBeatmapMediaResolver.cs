using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.Core;

public interface IBeatmapMediaResolver
{
    Task<BeatmapMedia> ResolveAsync(UnifiedBeatmapSet set, CancellationToken cancellationToken = default);
}
