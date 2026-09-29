using System.Text.Json;
using Android.Content;
using Android.Media;
using OsuMusicPlayer.Mobile.App;
using OsuMusicPlayer.Mobile.Core;

namespace OsuMusicPlayer.Mobile.Android;

internal sealed class AndroidMobileAudioService : IMobileAudioService
{
    private readonly HttpClient httpClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly SemaphoreSlim playbackGate = new(1, 1);
    private readonly string downloadRoot;
    private MediaPlayer? player;
    private bool disposed;

    public AndroidMobileAudioService(Context context)
    {
        var filesDirectory = context.FilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("アプリの保存先を取得できません。");
        downloadRoot = Path.Combine(filesDirectory, "music");
    }

    public Task PlayAsync(Uri audioUri, CancellationToken cancellationToken = default) =>
        StartAsync(audioUri.AbsoluteUri, cancellationToken);

    public async Task PlayDownloadedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var path = FindAudioPath(id) ?? throw new IOException("この曲は端末に保存されていません。");
        await StartAsync(path, cancellationToken);
    }

    private async Task StartAsync(string source, CancellationToken cancellationToken)
    {
        await playbackGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            player?.Release();
            player?.Dispose();
            player = null;

            var nextPlayer = new MediaPlayer();
            try
            {
                await Task.Run(() =>
                {
                    nextPlayer.SetDataSource(source);
                    nextPlayer.Prepare();
                }, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                nextPlayer.Start();
                player = nextPlayer;
            }
            catch
            {
                nextPlayer.Release();
                nextPlayer.Dispose();
                throw;
            }
        }
        finally
        {
            playbackGate.Release();
        }
    }

    public async Task ToggleAsync()
    {
        await playbackGate.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (player is null)
            {
                throw new InvalidOperationException("スマホで再生中の曲がありません。");
            }

            if (player.IsPlaying)
            {
                player.Pause();
            }
            else
            {
                player.Start();
            }
        }
        finally
        {
            playbackGate.Release();
        }
    }

    public async Task DownloadAsync(MobileTrack track, Uri audioUri, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var response = await httpClient.GetAsync(audioUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var extension = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() switch
        {
            "audio/ogg" => ".ogg",
            "audio/wav" or "audio/x-wav" => ".wav",
            "audio/flac" => ".flac",
            "audio/mp4" or "audio/m4a" => ".m4a",
            _ => ".mp3"
        };

        var folder = TrackFolder(track.Id);
        Directory.CreateDirectory(folder);
        var audioPath = Path.Combine(folder, "audio" + extension);
        var partialPath = Path.Combine(folder, "audio.partial");
        var metadataPath = Path.Combine(folder, "track.json");
        var metadataPartialPath = Path.Combine(folder, "track.partial");
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            if (new FileInfo(partialPath).Length == 0)
            {
                throw new InvalidDataException("空の音声ファイルを受信しました。");
            }

            File.Move(partialPath, audioPath, true);
            await using (var metadata = new FileStream(metadataPartialPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
            {
                await JsonSerializer.SerializeAsync(metadata, track, MobileJsonContext.Default.MobileTrack, cancellationToken);
            }

            File.Move(metadataPartialPath, metadataPath, true);
            foreach (var oldAudioPath in Directory.EnumerateFiles(folder, "audio.*"))
            {
                if (oldAudioPath != audioPath && !oldAudioPath.EndsWith(".partial", StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(oldAudioPath);
                }
            }
        }
        finally
        {
            File.Delete(partialPath);
            File.Delete(metadataPartialPath);
        }
    }

    public async Task<IReadOnlyList<MobileTrack>> GetDownloadedTracksAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(downloadRoot))
        {
            return [];
        }

        var tracks = new List<MobileTrack>();
        foreach (var folder in Directory.EnumerateDirectories(downloadRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(Path.GetFileName(folder), out var id) || FindAudioPath(id) is null)
            {
                continue;
            }

            try
            {
                await using var stream = new FileStream(Path.Combine(folder, "track.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
                var track = await JsonSerializer.DeserializeAsync(stream, MobileJsonContext.Default.MobileTrack, cancellationToken);
                if (track?.Id == id)
                {
                    tracks.Add(track);
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                // A missing or partially written entry must not hide other downloads.
            }
        }

        return tracks.OrderBy(static track => track.Artist).ThenBy(static track => track.Title).ToArray();
    }

    private string TrackFolder(Guid id) => Path.Combine(downloadRoot, id.ToString("D"));

    private string? FindAudioPath(Guid id)
    {
        var folder = TrackFolder(id);
        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "audio.*").FirstOrDefault(static path => !path.EndsWith(".partial", StringComparison.OrdinalIgnoreCase))
            : null;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        player?.Release();
        player?.Dispose();
        httpClient.Dispose();
        playbackGate.Dispose();
    }
}
