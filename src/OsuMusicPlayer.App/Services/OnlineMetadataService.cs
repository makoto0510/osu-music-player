using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OsuMusicPlayer.App.Services;

public sealed record OsuApiCredentials(string ClientId, string ClientSecret);

public sealed record BeatmapSetOnlineMetadata(long BeatmapSetId, string? Genre, string? Language, DateTime FetchedUtc);

public sealed record OnlineMetadataResult(long BeatmapSetId, BeatmapSetOnlineMetadata? Metadata);

public interface IOnlineMetadataService
{
    BeatmapSetOnlineMetadata? TryGetCached(long beatmapSetId);

    /// <summary>Fetches genre and language for the given sets, yielding as each one arrives; results are cached on disk.</summary>
    IAsyncEnumerable<OnlineMetadataResult> FetchAsync(OsuApiCredentials credentials, IReadOnlyList<long> beatmapSetIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// osu! API v2 client using the client-credentials grant. Requests are throttled well below
/// the API's rate limit and every answer is cached in the app's own data folder so a
/// library is only enriched once.
/// </summary>
public sealed class OnlineMetadataService : IOnlineMetadataService, IDisposable
{
    private static readonly Uri token_endpoint = new("https://osu.ppy.sh/oauth/token");
    private static readonly TimeSpan request_spacing = TimeSpan.FromMilliseconds(700);
    private static readonly JsonSerializerOptions json_options = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;
    private readonly string cachePath;
    private readonly ConcurrentDictionary<long, BeatmapSetOnlineMetadata> cache = new();
    private readonly SemaphoreSlim cacheGate = new(1, 1);
    private readonly bool ownsClient;
    private string? accessToken;
    private DateTime tokenExpiresUtc;
    private OsuApiCredentials? tokenCredentials;

    public OnlineMetadataService(string? cachePath = null)
        : this(new HttpClient { BaseAddress = new Uri("https://osu.ppy.sh/") }, cachePath, ownsClient: true)
    {
    }

    public OnlineMetadataService(HttpClient httpClient, string? cachePath = null, bool ownsClient = false)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.ownsClient = ownsClient;
        this.cachePath = cachePath ?? Path.Combine(Path.GetDirectoryName(JsonSettingsStore.GetDefaultFilePath()) ?? Path.GetTempPath(), "online-metadata.json");
        loadCache();
    }

    public BeatmapSetOnlineMetadata? TryGetCached(long beatmapSetId) => cache.GetValueOrDefault(beatmapSetId);

    public async IAsyncEnumerable<OnlineMetadataResult> FetchAsync(OsuApiCredentials credentials, IReadOnlyList<long> beatmapSetIds, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(beatmapSetIds);

        var dirty = false;
        foreach (var id in beatmapSetIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cache.TryGetValue(id, out var cached))
            {
                yield return new OnlineMetadataResult(id, cached);
                continue;
            }

            var token = await getTokenAsync(credentials, cancellationToken).ConfigureAwait(false);
            BeatmapSetOnlineMetadata? metadata = null;
            using (var request = new HttpRequestMessage(HttpMethod.Get, $"api/v2/beatmapsets/{id}"))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    accessToken = null;
                    throw new InvalidOperationException("osu! API rejected the credentials (401).");
                }

                if (response.IsSuccessStatusCode)
                {
                    var payload = await response.Content.ReadFromJsonAsync<BeatmapSetPayload>(json_options, cancellationToken).ConfigureAwait(false);
                    if (payload is not null)
                    {
                        metadata = new BeatmapSetOnlineMetadata(id, payload.Genre?.Name, payload.Language?.Name, DateTime.UtcNow);
                    }
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    metadata = new BeatmapSetOnlineMetadata(id, null, null, DateTime.UtcNow); // remember "unknown" too
                }
            }

            if (metadata is not null)
            {
                cache[id] = metadata;
                dirty = true;
            }

            yield return new OnlineMetadataResult(id, metadata);
            if (dirty && cache.Count % 20 == 0)
            {
                await saveCacheAsync(cancellationToken).ConfigureAwait(false);
                dirty = false;
            }

            await Task.Delay(request_spacing, cancellationToken).ConfigureAwait(false);
        }

        if (dirty)
        {
            await saveCacheAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (ownsClient)
        {
            httpClient.Dispose();
        }

        cacheGate.Dispose();
    }

    private async Task<string> getTokenAsync(OsuApiCredentials credentials, CancellationToken cancellationToken)
    {
        if (accessToken is not null && tokenCredentials == credentials && DateTime.UtcNow < tokenExpiresUtc - TimeSpan.FromMinutes(1))
        {
            return accessToken;
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = credentials.ClientId,
            ["client_secret"] = credentials.ClientSecret,
            ["grant_type"] = "client_credentials",
            ["scope"] = "public",
        });
        using var response = await httpClient.PostAsync(token_endpoint, content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"osu! API token request failed ({(int)response.StatusCode}). Check the client id and secret.");
        }

        var token = await response.Content.ReadFromJsonAsync<TokenPayload>(json_options, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("osu! API returned an empty token response.");
        accessToken = token.AccessToken;
        tokenExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn));
        tokenCredentials = credentials;
        return accessToken ?? throw new InvalidOperationException("osu! API returned no access token.");
    }

    private void loadCache()
    {
        try
        {
            if (!File.Exists(cachePath))
            {
                return;
            }

            var entries = JsonSerializer.Deserialize<List<BeatmapSetOnlineMetadata>>(File.ReadAllText(cachePath), json_options);
            foreach (var entry in entries ?? [])
            {
                cache[entry.BeatmapSetId] = entry;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged cache is simply rebuilt by the next fetch.
        }
    }

    private async Task saveCacheAsync(CancellationToken cancellationToken)
    {
        await cacheGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(cache.Values.OrderBy(static entry => entry.BeatmapSetId).ToArray(), json_options);
            await File.WriteAllTextAsync(cachePath + ".tmp", json, cancellationToken).ConfigureAwait(false);
            File.Move(cachePath + ".tmp", cachePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            cacheGate.Release();
        }
    }

    private sealed record TokenPayload([property: JsonPropertyName("access_token")] string? AccessToken, [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record NamedPayload([property: JsonPropertyName("name")] string? Name);

    private sealed record BeatmapSetPayload([property: JsonPropertyName("genre")] NamedPayload? Genre, [property: JsonPropertyName("language")] NamedPayload? Language);
}
