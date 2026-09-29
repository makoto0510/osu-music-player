using System.Globalization;
using System.Net.Http.Json;

namespace OsuMusicPlayer.Mobile.Core;

/// <summary>Typed client for the desktop server's LAN API.</summary>
public sealed class MobileServerClient
{
    private readonly HttpClient httpClient;
    private readonly Uri baseAddress;

    public MobileServerClient(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (httpClient.BaseAddress is not { IsAbsoluteUri: true } address ||
            (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps) ||
            address.AbsolutePath != "/" || address.Query.Length != 0 || address.Fragment.Length != 0)
        {
            throw new ArgumentException("An HTTP server root address is required.", nameof(httpClient));
        }

        baseAddress = address;
    }

    public async Task<MobileTrackPage> GetTracksAsync(string? query = null, int offset = 0, int limit = 100, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 500);

        var path = $"api/tracks?offset={offset}&limit={limit}";
        if (!string.IsNullOrWhiteSpace(query))
        {
            path += $"&q={Uri.EscapeDataString(query.Trim())}";
        }

        return await httpClient.GetFromJsonAsync(path, MobileJsonContext.Default.MobileTrackPage, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The server returned an empty track page.");
    }

    public async Task<MobilePlayerState> GetStateAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync("api/state", MobileJsonContext.Default.MobilePlayerState, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The server returned an empty player state.");

    public Task PlayAsync(Guid id, CancellationToken cancellationToken = default) => PostAsync($"api/play/{id:D}", cancellationToken);

    public Task ToggleAsync(CancellationToken cancellationToken = default) => PostAsync("api/toggle", cancellationToken);

    public Task NextAsync(CancellationToken cancellationToken = default) => PostAsync("api/next", cancellationToken);

    public Task PreviousAsync(CancellationToken cancellationToken = default) => PostAsync("api/previous", cancellationToken);

    public Task EnqueueAsync(Guid id, CancellationToken cancellationToken = default) => PostAsync($"api/queue/{id:D}", cancellationToken);

    public Task ToggleFavouriteAsync(Guid id, CancellationToken cancellationToken = default) => PostAsync($"api/favourite/{id:D}", cancellationToken);

    public Task SeekAsync(double seconds, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        return PostAsync($"api/seek?seconds={seconds.ToString(CultureInfo.InvariantCulture)}", cancellationToken);
    }

    public Task SetVolumeAsync(double value, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return PostAsync($"api/volume?value={value.ToString(CultureInfo.InvariantCulture)}", cancellationToken);
    }

    public Uri GetAudioUri(Guid id) => new(baseAddress, $"api/tracks/{id:D}/audio");

    public Uri GetBackgroundUri(Guid id) => new(baseAddress, $"api/tracks/{id:D}/background");

    private async Task PostAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync(path, null, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}
