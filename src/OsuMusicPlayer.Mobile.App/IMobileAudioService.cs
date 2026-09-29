using OsuMusicPlayer.Mobile.Core;

namespace OsuMusicPlayer.Mobile.App;

public interface IMobileAudioService : IDisposable
{
    Task PlayAsync(Uri audioUri, CancellationToken cancellationToken = default);
    Task ToggleAsync();
    Task DownloadAsync(MobileTrack track, Uri audioUri, CancellationToken cancellationToken = default);
    Task PlayDownloadedAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MobileTrack>> GetDownloadedTracksAsync(CancellationToken cancellationToken = default);
}
