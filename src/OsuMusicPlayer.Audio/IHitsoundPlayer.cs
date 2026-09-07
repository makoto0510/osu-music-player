using OsuMusicPlayer.Core.Hitsounds;
using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.Audio;

/// <summary>
/// Plays a beatmap's hit sounds in time with the audio engine's clock. Samples are
/// pre-decoded when a timeline is loaded so playback never touches the disk.
/// </summary>
public interface IHitsoundPlayer : IDisposable
{
    /// <summary>Raised (on the audio worker thread) for every hit whose time has come, so storyboard triggers can follow.</summary>
    event EventHandler<HitsoundEvent>? HitPlayed;

    bool IsEnabled { get; set; }

    /// <summary>Effective hit sound volume from 0 to 1. The caller applies any master-volume scaling.</summary>
    float Volume { get; set; }

    /// <summary>Manual timing correction in milliseconds; positive plays samples later.</summary>
    int OffsetMs { get; set; }

    /// <summary>Number of samples that could not be resolved by the last load.</summary>
    int MissingSampleCount { get; }

    Task LoadAsync(IReadOnlyList<HitsoundEvent> events, HitsoundSampleResolver resolver, CancellationToken cancellationToken = default);

    void Clear();
}
