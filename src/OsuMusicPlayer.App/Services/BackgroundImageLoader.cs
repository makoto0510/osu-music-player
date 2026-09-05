using Avalonia.Media.Imaging;

namespace OsuMusicPlayer.App.Services;

public sealed class BackgroundImageLoader : IBackgroundImageLoader
{
    public async Task<Bitmap?> LoadAsync(string? path, CancellationToken cancellationToken = default, int decodeHeight = 96)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var height = Math.Clamp(decodeHeight, 16, 2048);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    4096,
                    FileOptions.SequentialScan);
                return Bitmap.DecodeToHeight(stream, height, BitmapInterpolationMode.MediumQuality);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
