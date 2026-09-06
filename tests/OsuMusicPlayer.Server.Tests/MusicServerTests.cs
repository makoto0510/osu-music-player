using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace OsuMusicPlayer.Server.Tests;

public sealed class MusicServerTests
{
    [Fact]
    public async Task Server_ServesLibraryStateStreamingAndControls()
    {
        var audioPath = Path.Combine(Path.GetTempPath(), $"osu-server-test-{Guid.NewGuid():N}.mp3");
        await File.WriteAllBytesAsync(audioPath, Enumerable.Range(0, 1000).Select(static i => (byte)i).ToArray());
        var bridge = new FakeBridge(audioPath);
        await using var server = new MusicServer();
        try
        {
            await server.StartAsync(new MusicServerOptions { Port = 0, AllowRemoteConnections = false }, bridge);
            server.IsRunning.Should().BeTrue();
            server.Port.Should().BeGreaterThan(0);
            server.Urls.Should().ContainSingle().Which.Should().Be($"http://localhost:{server.Port}/");
            using var client = new HttpClient { BaseAddress = new Uri(server.Urls[0]) };

            var page = await client.GetStringAsync("/");
            page.Should().Contain("<title>osu! music player</title>");
            (await client.GetStringAsync("/overlay")).Should().Contain("overlay");

            var list = await client.GetFromJsonAsync<JsonElement>("/api/tracks?q=camellia&limit=10");
            list.GetProperty("total").GetInt32().Should().Be(1);
            var item = list.GetProperty("items")[0];
            item.GetProperty("title").GetString().Should().Be("Exit");
            item.TryGetProperty("audioPath", out _).Should().BeFalse("file paths never leave the server");

            var state = await client.GetFromJsonAsync<JsonElement>("/api/state");
            state.GetProperty("isPlaying").GetBoolean().Should().BeFalse();

            (await client.PostAsync($"/api/play/{bridge.Tracks[0].Id}", null)).StatusCode.Should().Be(HttpStatusCode.OK);
            bridge.Played.Should().Equal(bridge.Tracks[0].Id);
            (await client.PostAsync($"/api/play/{Guid.NewGuid()}", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.PostAsync("/api/volume?value=0.25", null)).StatusCode.Should().Be(HttpStatusCode.OK);
            bridge.Volume.Should().Be(0.25);
            (await client.PostAsync("/api/seek?seconds=-1", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await client.PostAsync($"/api/favourite/{bridge.Tracks[1].Id}", null)).StatusCode.Should().Be(HttpStatusCode.OK);
            bridge.Favourited.Should().Equal(bridge.Tracks[1].Id);

            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/tracks/{bridge.Tracks[0].Id}/audio");
            request.Headers.Range = new RangeHeaderValue(100, 199);
            using var response = await client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.PartialContent);
            response.Content.Headers.ContentType!.MediaType.Should().Be("audio/mpeg");
            (await response.Content.ReadAsByteArrayAsync()).Should().HaveCount(100).And.StartWith(new byte[] { 100, 101, 102 });

            (await client.GetAsync($"/api/tracks/{bridge.Tracks[1].Id}/audio")).StatusCode.Should().Be(HttpStatusCode.NotFound, "the second track has no file");

            await server.StopAsync();
            server.IsRunning.Should().BeFalse();
        }
        finally
        {
            File.Delete(audioPath);
        }
    }

    [Fact]
    public void ContentTypes_AreSniffedForExtensionlessLazerFiles()
    {
        var ogg = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(ogg, "OggS...."u8.ToArray());
        var png = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(png, [0x89, (byte)'P', (byte)'N', (byte)'G', 0, 0]);
        try
        {
            MusicServer.audioContentType(ogg).Should().Be("audio/ogg");
            MusicServer.audioContentType("song.MP3").Should().Be("audio/mpeg");
            MusicServer.imageContentType(png).Should().Be("image/png");
            MusicServer.imageContentType("missing.jpg").Should().Be("image/jpeg");
        }
        finally
        {
            File.Delete(ogg);
            File.Delete(png);
        }
    }

    private sealed class FakeBridge : IPlayerBridge
    {
        public FakeBridge(string audioPath)
        {
            Tracks =
            [
                new ServerTrack(Guid.NewGuid(), "Exit", "Camellia", "Mapper", 200, 240, 123, false, "stable", false) { AudioPath = audioPath },
                new ServerTrack(Guid.NewGuid(), "Idol", "YOASOBI", "Mapper", 166, 213, null, true, "lazer", false),
            ];
        }

        public List<ServerTrack> Tracks { get; }
        public List<Guid> Played { get; } = [];
        public List<Guid> Favourited { get; } = [];
        public double Volume { get; private set; } = 1;

        public Task<IReadOnlyList<ServerTrack>> GetTracksAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ServerTrack>>(Tracks);
        public Task<ServerTrack?> GetTrackAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Tracks.FirstOrDefault(track => track.Id == id));
        public Task<PlayerState> GetStateAsync(CancellationToken cancellationToken) => Task.FromResult(new PlayerState(null, false, 0, 0, Volume, "None", false, "Off", []));
        public Task<bool> PlayAsync(Guid id, CancellationToken cancellationToken) { var ok = Tracks.Any(track => track.Id == id); if (ok) { Played.Add(id); } return Task.FromResult(ok); }
        public Task TogglePlayAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task NextAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PreviousAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SeekAsync(double seconds, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetVolumeAsync(double volume, CancellationToken cancellationToken) { Volume = volume; return Task.CompletedTask; }
        public Task<bool> EnqueueAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Tracks.Any(track => track.Id == id));
        public Task<bool> ToggleFavouriteAsync(Guid id, CancellationToken cancellationToken) { var ok = Tracks.Any(track => track.Id == id); if (ok) { Favourited.Add(id); } return Task.FromResult(ok); }
    }
}
