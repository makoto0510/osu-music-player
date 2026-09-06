using Microsoft.Extensions.Logging;
using OsuMusicPlayer.Core;
using OsuMusicPlayer.Core.Loaders;
using OsuMusicPlayer.Server;

// Headless music server for an always-on box (Raspberry Pi, home server). It reads an
// osu!lazer (or, on Windows, osu!stable) library and serves the same web remote / API as
// the desktop app, minus local playback: phones stream the audio themselves.
//
//   osu-music-server [--port 5150] [--local-only] [--lazer <folder>] [--stable <folder>]

var port = 5150;
var allowRemote = true;
var manual = new List<OsuInstallation>();
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port" when i + 1 < args.Length && int.TryParse(args[++i], out var parsedPort):
            port = parsedPort;
            break;
        case "--local-only":
            allowRemote = false;
            break;
        case "--lazer" when i + 1 < args.Length:
            manual.Add(new OsuInstallation(OsuInstallationKind.Lazer, args[++i]));
            break;
        case "--stable" when i + 1 < args.Length:
            manual.Add(new OsuInstallation(OsuInstallationKind.Stable, args[++i]));
            break;
        case "--help":
        case "-h":
            Console.WriteLine("usage: osu-music-server [--port 5150] [--local-only] [--lazer <folder>] [--stable <folder>]");
            return 0;
        default:
            Console.Error.WriteLine($"unknown argument: {args[i]}");
            return 2;
    }
}

using var loggerFactory = LoggerFactory.Create(static builder => builder.AddSimpleConsole(static options => options.SingleLine = true).SetMinimumLevel(LogLevel.Information));
var log = loggerFactory.CreateLogger("osu-music-server");

var locator = new OsuInstallationLocator();
var installations = new List<OsuInstallation>(locator.FindInstallations());
foreach (var candidate in manual)
{
    var validated = locator.ValidateManualPath(candidate.RootPath, candidate.Kind);
    if (validated is null)
    {
        log.LogError("{Kind} folder is not valid: {Path}", candidate.Kind, candidate.RootPath);
        return 1;
    }

    installations.Add(validated);
}

if (installations.Count == 0)
{
    log.LogError("No osu! installation found. Pass --lazer <folder> (client.realm + files) or --stable <folder>.");
    return 1;
}

foreach (var installation in installations)
{
    log.LogInformation("Library source: {Kind} {Path}", installation.Kind, installation.RootPath);
}

var manager = new BeatmapManager(
    [new OsuStableLoader(loggerFactory.CreateLogger<OsuStableLoader>()), new OsuLazerLoader(loggerFactory.CreateLogger<OsuLazerLoader>())],
    new DuplicateDetector());
var sets = await manager.LoadAsync(installations);
var bridge = new LibraryPlayerBridge(sets);
log.LogInformation("Loaded {Count} tracks.", bridge.TrackCount);

await using var server = new MusicServer();
using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

try
{
    await server.StartAsync(new MusicServerOptions { Port = port, AllowRemoteConnections = allowRemote }, bridge, shutdown.Token);
}
catch (Exception exception) when (exception is IOException or InvalidOperationException or System.Net.Sockets.SocketException)
{
    log.LogError("Could not start the server on port {Port}: {Message}", port, exception.Message);
    return 1;
}

foreach (var url in server.Urls)
{
    log.LogInformation("Listening on {Url}  (overlay: {Url}overlay)", url, url);
}

log.LogInformation("Press Ctrl+C to stop.");
try
{
    await Task.Delay(Timeout.Infinite, shutdown.Token);
}
catch (OperationCanceledException)
{
}

await server.StopAsync();
log.LogInformation("Stopped.");
return 0;
