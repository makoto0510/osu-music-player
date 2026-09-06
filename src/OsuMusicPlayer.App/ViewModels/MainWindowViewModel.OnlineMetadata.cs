using OsuMusicPlayer.App.Services;

namespace OsuMusicPlayer.App.ViewModels;

/// <summary>Genre and language enrichment through the osu! API (opt-in, needs the user's OAuth client).</summary>
public sealed partial class MainWindowViewModel
{
    private async Task fetchOnlineMetadataAsync()
    {
        if (onlineMetadataService is null)
        {
            OnlineMetadataStatusText = "osu! API 連携はこのビルドでは無効です。";
            return;
        }

        if (string.IsNullOrWhiteSpace(OsuApiClientId) || string.IsNullOrWhiteSpace(OsuApiClientSecret))
        {
            OnlineMetadataStatusText = "Client ID と Client secret を入力してください。";
            return;
        }

        var targets = allTracks.Where(static track => track.Model.OnlineId is > 0 && !track.HasOnlineMetadata).ToArray();
        if (targets.Length == 0)
        {
            OnlineMetadataStatusText = "取得対象がありません(オンライン ID のある未取得トラックなし)。";
            return;
        }

        OnlineMetadataStatusText = $"取得中… 0 / {targets.Length:N0}";
        var done = 0;
        var failed = 0;
        try
        {
            var credentials = new OsuApiCredentials(OsuApiClientId.Trim(), OsuApiClientSecret.Trim());
            await foreach (var result in onlineMetadataService.FetchAsync(credentials, targets.Select(static track => track.Model.OnlineId!.Value).Distinct().ToArray(), lifetimeCancellation.Token).ConfigureAwait(false))
            {
                if (result.Metadata is null)
                {
                    failed++;
                }
                else
                {
                    done++;
                    var metadata = result.Metadata;
                    await dispatcher.InvokeAsync(() =>
                    {
                        foreach (var track in targets.Where(track => track.Model.OnlineId == result.BeatmapSetId))
                        {
                            track.Genre = metadata.Genre;
                            track.Language = metadata.Language;
                        }
                    }).ConfigureAwait(false);
                }

                if ((done + failed) % 10 == 0)
                {
                    await dispatcher.InvokeAsync(() => OnlineMetadataStatusText = $"取得中… {done + failed:N0} / {targets.Length:N0}").ConfigureAwait(false);
                }
            }

            await dispatcher.InvokeAsync(() =>
            {
                OnlineMetadataStatusText = failed == 0 ? $"{done:N0} 件取得しました。" : $"{done:N0} 件取得、{failed:N0} 件失敗。";
                rebuildViews();
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or IOException or System.Text.Json.JsonException)
        {
            await dispatcher.InvokeAsync(() => OnlineMetadataStatusText = $"取得に失敗しました: {exception.Message}").ConfigureAwait(false);
        }
    }

    private void applyCachedOnlineMetadata()
    {
        if (onlineMetadataService is null)
        {
            return;
        }

        foreach (var track in allTracks)
        {
            if (track.Model.OnlineId is > 0 && onlineMetadataService.TryGetCached(track.Model.OnlineId.Value) is { } cached)
            {
                track.Genre = cached.Genre;
                track.Language = cached.Language;
            }
        }
    }
}
