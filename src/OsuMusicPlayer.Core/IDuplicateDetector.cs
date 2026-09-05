using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.Core;

public interface IDuplicateDetector
{
    bool AreDuplicates(UnifiedBeatmapSet left, UnifiedBeatmapSet right);

    /// <summary>
    /// Returns a normalized metadata key used to bucket candidate duplicates, or
    /// <see langword="null"/> when the metadata is too sparse to match safely.
    /// Two sets with equal non-null keys are duplicates unless both carry
    /// different online ids.
    /// </summary>
    string? CreateMetadataKey(UnifiedBeatmapSet set);
}
