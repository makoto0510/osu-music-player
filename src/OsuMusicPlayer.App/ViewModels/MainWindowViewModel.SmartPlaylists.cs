using CommunityToolkit.Mvvm.Input;
using OsuMusicPlayer.App.Services;
using OsuMusicPlayer.Core;
using OsuMusicPlayer.Core.Collections;

namespace OsuMusicPlayer.App.ViewModels;

/// <summary>Saved searches ("smart playlists") and exporting a view as an osu! collection.</summary>
public sealed partial class MainWindowViewModel
{
    public bool IsSmartPlaylistViewSelected => SelectedView?.Kind == LibraryViewKind.SmartPlaylist;

    /// <summary>Saves the current search text as a smart playlist; the name defaults to the query itself.</summary>
    [RelayCommand]
    private void CreateSmartPlaylist()
    {
        var query = SearchText.Trim();
        if (query.Length == 0)
        {
            LibraryStatusText = "スマートプレイリストにする検索条件を検索欄に入力してください。";
            return;
        }

        var name = NewPlaylistName.Trim();
        if (name.Length == 0)
        {
            name = query;
        }

        var smart = new SmartPlaylistSetting(Guid.NewGuid(), name, query);
        SmartPlaylists.Add(smart);
        NewPlaylistName = string.Empty;
        SearchText = string.Empty;
        rebuildViews();
        SelectedView = Views.FirstOrDefault(view => view.Kind == LibraryViewKind.SmartPlaylist && view.Id == smart.Id);
        RequestSettingsSave();
    }

    [RelayCommand]
    private void DeleteSmartPlaylist()
    {
        if (SelectedView is not { Kind: LibraryViewKind.SmartPlaylist } view)
        {
            return;
        }

        var target = SmartPlaylists.FirstOrDefault(smart => smart.Id == view.Id);
        if (target is null)
        {
            return;
        }

        SmartPlaylists.Remove(target);
        rebuildViews();
        SelectedView = Views.FirstOrDefault();
        RequestSettingsSave();
    }

    /// <summary>
    /// Writes the tracks currently listed (view + search) as a collection.db the user can import
    /// into osu!stable. The file goes wherever the user picks, never into an osu! folder.
    /// </summary>
    [RelayCommand]
    private async Task ExportViewAsCollectionAsync()
    {
        if (fileSaver is null)
        {
            ErrorMessage = "このビルドではファイルの保存ダイアログを使えません。";
            return;
        }

        var tracks = Tracks.ToArray();
        if (tracks.Length == 0)
        {
            LibraryStatusText = "書き出すトラックがありません。";
            return;
        }

        var name = SelectedView?.Name ?? "osu! music player";
        string? path;
        try
        {
            path = await fileSaver.PickSavePathAsync("Export as osu! collection", "collection.db", lifetimeCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        OsuInstallation[] roots = [];
        await dispatcher.InvokeAsync(() => roots = Installations.Select(static item => item.Installation).ToArray()).ConfigureAwait(false);
        try
        {
            var hashes = tracks.SelectMany(static track => track.Model.Beatmaps).SelectMany(static beatmap => beatmap.AllMd5Hashes).ToArray();
            await Task.Run(() => CollectionExporter.Write(path, name, hashes, roots.Select(static root => root.RootPath)), lifetimeCancellation.Token).ConfigureAwait(false);
            await dispatcher.InvokeAsync(() =>
            {
                ErrorMessage = null;
                LibraryStatusText = $"コレクションを書き出しました: {path}({hashes.Length:N0} 譜面)。osu!stable では collection.db を置き換えるのではなく、osu! 上で読み込んでください。";
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await dispatcher.InvokeAsync(() => ErrorMessage = $"コレクションを書き出せませんでした: {exception.Message}").ConfigureAwait(false);
        }
    }
}
