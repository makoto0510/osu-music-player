using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OsuMusicPlayer.Server;

public sealed class MusicServerOptions
{
    /// <summary>TCP port; 0 picks a free port (used by tests).</summary>
    public int Port { get; init; } = 5150;

    /// <summary>Listen on every interface (phones on the LAN) instead of loopback only.</summary>
    public bool AllowRemoteConnections { get; init; } = true;
}

/// <summary>
/// Kestrel host for the LAN music server: a JSON API over the library and player, audio
/// streaming with range support, a phone-friendly web remote and an OBS overlay page.
/// The osu! folders are only ever read.
/// </summary>
public sealed class MusicServer : IAsyncDisposable
{
    private const int max_page_size = 500;

    private WebApplication? application;

    public bool IsRunning => application is not null;

    public int Port { get; private set; }

    /// <summary>Addresses clients can use, localhost first, then LAN addresses when remote access is on.</summary>
    public IReadOnlyList<string> Urls { get; private set; } = [];

    public async Task StartAsync(MusicServerOptions options, IPlayerBridge bridge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(bridge);
        if (application is not null)
        {
            throw new InvalidOperationException("The server is already running.");
        }

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.Configure<JsonOptions>(static json =>
        {
            json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            json.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            if (options.AllowRemoteConnections)
            {
                kestrel.ListenAnyIP(options.Port);
            }
            else
            {
                // Listen(...) rather than ListenLocalhost(...) so port 0 (tests) works too.
                kestrel.Listen(IPAddress.Loopback, options.Port);
            }
        });

        var app = builder.Build();
        mapRoutes(app, bridge);
        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        application = app;

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        Port = addresses.Select(static address => Uri.TryCreate(address.Replace("*", "localhost", StringComparison.Ordinal).Replace("[::]", "localhost", StringComparison.Ordinal).Replace("0.0.0.0", "localhost", StringComparison.Ordinal), UriKind.Absolute, out var uri) ? uri.Port : 0).FirstOrDefault(static port => port > 0);
        if (Port == 0)
        {
            Port = options.Port;
        }

        var urls = new List<string> { $"http://localhost:{Port}/" };
        if (options.AllowRemoteConnections)
        {
            urls.AddRange(localAddresses().Select(address => $"http://{address}:{Port}/"));
        }

        Urls = urls;
    }

    public async Task StopAsync()
    {
        if (application is not { } app)
        {
            return;
        }

        application = null;
        Urls = [];
        try
        {
            await app.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private static void mapRoutes(WebApplication app, IPlayerBridge bridge)
    {
        app.MapGet("/", static () => htmlPage("index.html"));
        app.MapGet("/overlay", static () => htmlPage("overlay.html"));

        app.MapGet("/api/tracks", async (string? q, int? offset, int? limit, CancellationToken cancellationToken) =>
        {
            var tracks = await bridge.GetTracksAsync(cancellationToken).ConfigureAwait(false);
            IEnumerable<ServerTrack> query = tracks;
            if (!string.IsNullOrWhiteSpace(q))
            {
                var terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                query = query.Where(track => terms.All(term =>
                    track.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    track.Artist.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    track.Creator.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (track.TitleRomanised?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (track.ArtistRomanised?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)));
            }

            var skip = Math.Max(0, offset ?? 0);
            var take = Math.Clamp(limit ?? 100, 1, max_page_size);
            var page = query.Skip(skip).Take(take).ToArray();
            return Results.Ok(new { total = query.Count(), offset = skip, items = page });
        });

        app.MapGet("/api/tracks/{id:guid}", async (Guid id, CancellationToken cancellationToken) =>
            await bridge.GetTrackAsync(id, cancellationToken).ConfigureAwait(false) is { } track ? Results.Ok(track) : Results.NotFound());

        app.MapGet("/api/tracks/{id:guid}/audio", async (Guid id, CancellationToken cancellationToken) =>
        {
            var track = await bridge.GetTrackAsync(id, cancellationToken).ConfigureAwait(false);
            if (track?.AudioPath is not { } path || !File.Exists(path))
            {
                return Results.NotFound();
            }

            return Results.File(path, audioContentType(path), enableRangeProcessing: true);
        });

        app.MapGet("/api/tracks/{id:guid}/background", async (Guid id, CancellationToken cancellationToken) =>
        {
            var track = await bridge.GetTrackAsync(id, cancellationToken).ConfigureAwait(false);
            if (track?.BackgroundPath is not { } path || !File.Exists(path))
            {
                return Results.NotFound();
            }

            return Results.File(path, imageContentType(path), enableRangeProcessing: false);
        });

        app.MapGet("/api/state", async (CancellationToken cancellationToken) => Results.Ok(await bridge.GetStateAsync(cancellationToken).ConfigureAwait(false)));

        app.MapPost("/api/play/{id:guid}", async (Guid id, CancellationToken cancellationToken) =>
            await bridge.PlayAsync(id, cancellationToken).ConfigureAwait(false) ? Results.Ok() : Results.NotFound());
        app.MapPost("/api/toggle", async (CancellationToken cancellationToken) => { await bridge.TogglePlayAsync(cancellationToken).ConfigureAwait(false); return Results.Ok(); });
        app.MapPost("/api/pause", async (CancellationToken cancellationToken) => { await bridge.PauseAsync(cancellationToken).ConfigureAwait(false); return Results.Ok(); });
        app.MapPost("/api/resume", async (CancellationToken cancellationToken) => { await bridge.ResumeAsync(cancellationToken).ConfigureAwait(false); return Results.Ok(); });
        app.MapPost("/api/next", async (CancellationToken cancellationToken) => { await bridge.NextAsync(cancellationToken).ConfigureAwait(false); return Results.Ok(); });
        app.MapPost("/api/previous", async (CancellationToken cancellationToken) => { await bridge.PreviousAsync(cancellationToken).ConfigureAwait(false); return Results.Ok(); });
        app.MapPost("/api/seek", async (double seconds, CancellationToken cancellationToken) =>
        {
            if (!double.IsFinite(seconds) || seconds < 0)
            {
                return Results.BadRequest();
            }

            await bridge.SeekAsync(seconds, cancellationToken).ConfigureAwait(false);
            return Results.Ok();
        });
        app.MapPost("/api/volume", async (double value, CancellationToken cancellationToken) =>
        {
            if (!double.IsFinite(value))
            {
                return Results.BadRequest();
            }

            await bridge.SetVolumeAsync(Math.Clamp(value, 0, 1), cancellationToken).ConfigureAwait(false);
            return Results.Ok();
        });
        app.MapPost("/api/queue/{id:guid}", async (Guid id, CancellationToken cancellationToken) =>
            await bridge.EnqueueAsync(id, cancellationToken).ConfigureAwait(false) ? Results.Ok() : Results.NotFound());
        app.MapPost("/api/favourite/{id:guid}", async (Guid id, CancellationToken cancellationToken) =>
            await bridge.ToggleFavouriteAsync(id, cancellationToken).ConfigureAwait(false) ? Results.Ok() : Results.NotFound());
    }

    private static IResult htmlPage(string resourceName)
    {
        using var stream = typeof(MusicServer).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return Results.NotFound();
        }

        using var reader = new StreamReader(stream);
        return Results.Content(reader.ReadToEnd(), "text/html; charset=utf-8");
    }

    internal static string audioContentType(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".mp3":
                return "audio/mpeg";
            case ".ogg":
            case ".oga":
                return "audio/ogg";
            case ".wav":
                return "audio/wav";
            case ".m4a":
            case ".mp4":
                return "audio/mp4";
            case ".flac":
                return "audio/flac";
        }

        // lazer stores files by hash without an extension: sniff the header.
        var header = readHeader(path, 4);
        if (header.Length >= 4 && header[0] == (byte)'O' && header[1] == (byte)'g' && header[2] == (byte)'g' && header[3] == (byte)'S')
        {
            return "audio/ogg";
        }

        if (header.Length >= 4 && header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F')
        {
            return "audio/wav";
        }

        if (header.Length >= 4 && header[0] == (byte)'f' && header[1] == (byte)'L' && header[2] == (byte)'a' && header[3] == (byte)'C')
        {
            return "audio/flac";
        }

        return "audio/mpeg";
    }

    internal static string imageContentType(string path)
    {
        var header = readHeader(path, 4);
        if (header.Length >= 4 && header[0] == 0x89 && header[1] == (byte)'P' && header[2] == (byte)'N' && header[3] == (byte)'G')
        {
            return "image/png";
        }

        if (header.Length >= 3 && header[0] == (byte)'G' && header[1] == (byte)'I' && header[2] == (byte)'F')
        {
            return "image/gif";
        }

        return "image/jpeg";
    }

    private static byte[] readHeader(string path, int count)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 16);
            var buffer = new byte[count];
            var read = stream.Read(buffer, 0, count);
            return buffer[..read];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> localAddresses()
    {
        var result = new List<string>();
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(unicast.Address))
                    {
                        result.Add(unicast.Address.ToString());
                    }
                }
            }
        }
        catch (NetworkInformationException)
        {
        }

        return result.Distinct();
    }
}
