using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OsuMusicPlayer.Mobile.Core;

namespace OsuMusicPlayer.Mobile.App;

public partial class MobileViewModel : ObservableObject, IDisposable
{
    private readonly IMobileAudioService? audioService;
    private HttpClient? httpClient;
    private MobileServerClient? server;

    public ObservableCollection<MobileTrack> Tracks { get; } = [];
    public ObservableCollection<MobileTrack> DownloadedTracks { get; } = [];

    public MobileViewModel(IMobileAudioService? audioService = null)
    {
        this.audioService = audioService;
        _ = LoadDownloadsAsync();
    }

    [ObservableProperty]
    private string serverAddress = string.Empty;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string status = "PC の設定でサーバーと LAN 接続の許可（Allow LAN）を有効にしてください。";

    [ObservableProperty]
    private string currentTitle = "再生中の曲はありません";

    [ObservableProperty]
    private MobileTrack? selectedTrack;

    [ObservableProperty]
    private MobileTrack? selectedDownloadedTrack;

    [ObservableProperty]
    private string phoneTitle = "スマホでは再生していません";

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (!Uri.TryCreate(ServerAddress.Trim(), UriKind.Absolute, out var address) ||
            (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps) ||
            address.AbsolutePath != "/" || address.Query.Length != 0 || address.Fragment.Length != 0)
        {
            Status = "サーバーのURLを http://192.168.1.10:5150/ の形式で入力してください。";
            return;
        }

        var nextHttpClient = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(10) };
        var nextServer = new MobileServerClient(nextHttpClient);
        try
        {
            var page = await nextServer.GetTracksAsync().ConfigureAwait(true);
            var state = await nextServer.GetStateAsync().ConfigureAwait(true);
            httpClient?.Dispose();
            httpClient = nextHttpClient;
            server = nextServer;
            ReplaceTracks(page.Items);
            CurrentTitle = state.Current is { } track ? $"{track.Artist} — {track.Title}" : "再生中の曲はありません";
            Status = $"接続済み · {page.Total} 曲";
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidDataException or JsonException)
        {
            nextHttpClient.Dispose();
            Status = $"接続できません: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (server is null)
        {
            Status = "先にサーバーへ接続してください。";
            return;
        }

        try
        {
            var page = await server.GetTracksAsync(SearchText).ConfigureAwait(true);
            ReplaceTracks(page.Items);
            Status = $"検索結果: {page.Total} 曲（先頭 {page.Items.Count} 曲を表示）";
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidDataException or JsonException)
        {
            Status = $"検索に失敗しました: {exception.Message}";
        }
    }

    [RelayCommand]
    private Task PlaySelectedAsync() => ExecuteAsync(client => SelectedTrack is { } track
        ? client.PlayAsync(track.Id)
        : Task.CompletedTask);

    [RelayCommand]
    private Task ToggleAsync() => ExecuteAsync(client => client.ToggleAsync());

    [RelayCommand]
    private Task NextAsync() => ExecuteAsync(client => client.NextAsync());

    [RelayCommand]
    private Task PreviousAsync() => ExecuteAsync(client => client.PreviousAsync());

    [RelayCommand]
    private async Task PlayOnPhoneAsync()
    {
        if (audioService is null || SelectedTrack is not { } track)
        {
            Status = "再生する曲を選択してください。";
            return;
        }

        try
        {
            if (server is not null)
            {
                await audioService.PlayAsync(server.GetAudioUri(track.Id));
            }
            else
            {
                await audioService.PlayDownloadedAsync(track.Id);
            }

            PhoneTitle = $"スマホで再生中: {track.Artist} — {track.Title}";
        }
        catch (Exception exception)
        {
            Status = $"スマホで再生できません: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task TogglePhoneAsync()
    {
        if (audioService is null)
        {
            Status = "スマホの音声再生を利用できません。";
            return;
        }

        try
        {
            await audioService.ToggleAsync();
        }
        catch (Exception exception)
        {
            Status = $"スマホの再生操作に失敗しました: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task DownloadSelectedAsync()
    {
        if (audioService is null || server is null || SelectedTrack is not { } track)
        {
            Status = "サーバーに接続して曲を選択してください。";
            return;
        }

        try
        {
            Status = $"ダウンロード中: {track.Artist} — {track.Title}";
            await audioService.DownloadAsync(track, server.GetAudioUri(track.Id));
            await LoadDownloadsAsync();
            Status = $"アプリ内に保存しました: {track.Artist} — {track.Title}";
        }
        catch (Exception exception)
        {
            Status = $"ダウンロードに失敗しました: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task PlayDownloadedAsync()
    {
        if (audioService is null || (SelectedDownloadedTrack ?? SelectedTrack) is not { } track)
        {
            Status = "保存済みの曲を選択してください。";
            return;
        }

        try
        {
            await audioService.PlayDownloadedAsync(track.Id);
            PhoneTitle = $"スマホで再生中: {track.Artist} — {track.Title}";
        }
        catch (Exception exception)
        {
            Status = $"保存済みの曲を再生できません: {exception.Message}";
        }
    }

    private async Task LoadDownloadsAsync()
    {
        if (audioService is null)
        {
            return;
        }

        try
        {
            var tracks = await audioService.GetDownloadedTracksAsync();
            DownloadedTracks.Clear();
            foreach (var track in tracks)
            {
                DownloadedTracks.Add(track);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status = $"保存済みの曲を読み込めません: {exception.Message}";
        }
    }

    private async Task ExecuteAsync(Func<MobileServerClient, Task> command)
    {
        if (server is null)
        {
            Status = "先にサーバーへ接続してください。";
            return;
        }

        try
        {
            await command(server).ConfigureAwait(true);
            var state = await server.GetStateAsync().ConfigureAwait(true);
            CurrentTitle = state.Current is { } track ? $"{track.Artist} — {track.Title}" : "再生中の曲はありません";
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidDataException or JsonException)
        {
            Status = $"操作に失敗しました: {exception.Message}";
        }
    }

    private void ReplaceTracks(IReadOnlyList<MobileTrack> tracks)
    {
        Tracks.Clear();
        foreach (var track in tracks)
        {
            Tracks.Add(track);
        }
    }

    public void Dispose()
    {
        httpClient?.Dispose();
        audioService?.Dispose();
    }
}
