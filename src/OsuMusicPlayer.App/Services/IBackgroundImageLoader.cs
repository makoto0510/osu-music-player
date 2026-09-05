using Avalonia.Media.Imaging;

namespace OsuMusicPlayer.App.Services;

public interface IBackgroundImageLoader
{
    /// <summary>
    /// Decodes a background image scaled to <paramref name="decodeHeight"/> pixels. Returns
    /// <see langword="null"/> for missing, unreadable or cancelled loads.
    /// </summary>
    Task<Bitmap?> LoadAsync(string? path, CancellationToken cancellationToken = default, int decodeHeight = 96);
}
