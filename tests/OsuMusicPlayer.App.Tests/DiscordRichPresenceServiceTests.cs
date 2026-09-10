using System.Text;
using System.Text.Json;
using OsuMusicPlayer.App.Services;

namespace OsuMusicPlayer.App.Tests;

public sealed class DiscordRichPresenceServiceTests
{
    [Theory]
    [InlineData(93398L, "https://assets.ppy.sh/beatmaps/93398/covers/cover.jpg")]
    [InlineData(null, null)]
    [InlineData(0L, null)]
    [InlineData(-1L, null)]
    public void Background_UsesPublicSetCoverAndOmitsUnsubmittedMaps(long? setId, string? expected)
    {
        var url = RichPresenceHostService.BuildBackgroundUrl(setId);
        url.Should().Be(expected);
        using var json = JsonDocument.Parse(DiscordRichPresenceService.BuildActivityJson(
            new RichPresenceActivity("Song", "Artist", null, "mapped by X", LargeImage: url), 1, "n"));
        var assets = json.RootElement.GetProperty("args").GetProperty("activity").GetProperty("assets");
        assets.GetProperty("large_text").GetString().Should().Be("mapped by X");
        if (expected is null)
        {
            assets.TryGetProperty("large_image", out _).Should().BeFalse();
        }
        else
        {
            assets.GetProperty("large_image").GetString().Should().Be(expected);
        }
    }

    [Fact]
    public async Task Frames_RoundTrip()
    {
        var frame = DiscordRichPresenceService.EncodeFrame(1, "{\"a\":1}");
        frame.Should().HaveCount(8 + 7);
        frame[0].Should().Be(1);
        frame[4].Should().Be(7);

        using var stream = new MemoryStream(frame);
        var decoded = await DiscordRichPresenceService.ReadFrameAsync(stream, CancellationToken.None);

        decoded.Should().NotBeNull();
        decoded!.Value.Opcode.Should().Be(1);
        decoded.Value.Json.Should().Be("{\"a\":1}");
        (await DiscordRichPresenceService.ReadFrameAsync(stream, CancellationToken.None)).Should().BeNull("the stream is exhausted");
    }

    [Fact]
    public void ActivityJson_FollowsTheDiscordShape()
    {
        var started = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var json = DiscordRichPresenceService.BuildActivityJson(new RichPresenceActivity("Title", "▶ Artist", started, "mapped by X", started.AddMinutes(3)), 4321, "nonce-1");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("cmd").GetString().Should().Be("SET_ACTIVITY");
        root.GetProperty("args").GetProperty("pid").GetInt32().Should().Be(4321);
        var activity = root.GetProperty("args").GetProperty("activity");
        activity.GetProperty("type").GetInt32().Should().Be(2);
        activity.GetProperty("timestamps").GetProperty("end").GetInt64().Should().Be(1_700_000_180);
        activity.GetProperty("details").GetString().Should().Be("Title");
        activity.GetProperty("state").GetString().Should().Be("▶ Artist");
        activity.GetProperty("timestamps").GetProperty("start").GetInt64().Should().Be(1_700_000_000);
        activity.GetProperty("assets").GetProperty("large_text").GetString().Should().Be("mapped by X");
        root.GetProperty("nonce").GetString().Should().Be("nonce-1");

        var cleared = DiscordRichPresenceService.BuildActivityJson(null, 1, "n");
        JsonDocument.Parse(cleared).RootElement.GetProperty("args").TryGetProperty("activity", out _).Should().BeFalse("null activity clears presence");
    }

    [Theory]
    [InlineData(Audio.OsuAudioMod.None, 60, 120)]
    [InlineData(Audio.OsuAudioMod.DT, 40, 80)]
    [InlineData(Audio.OsuAudioMod.NC, 40, 80)]
    [InlineData(Audio.OsuAudioMod.HT, 80, 160)]
    [InlineData(Audio.OsuAudioMod.DC, 80, 160)]
    public void Timeline_AccountsForPositionAndPlaybackRate(Audio.OsuAudioMod mod, int elapsed, int remaining)
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var timeline = RichPresenceHostService.BuildTimestamps(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(180), true, mod, now);
        timeline.Start.Should().Be(now.AddSeconds(-elapsed));
        timeline.End.Should().Be(now.AddSeconds(remaining));
        var sought = RichPresenceHostService.BuildTimestamps(TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(180), true, mod, now);
        sought.Start.Should().Be(now.AddSeconds(-elapsed * 2));
        sought.End.Should().Be(now.AddSeconds(remaining / 2.0));
    }

    [Theory]
    [InlineData(false, 180, 60)]
    [InlineData(true, 0, 60)]
    public void PausedOrUnknownDuration_OmitsAdvancingTimer(bool playing, int duration, int position)
    {
        var timeline = RichPresenceHostService.BuildTimestamps(TimeSpan.FromSeconds(position), TimeSpan.FromSeconds(duration), playing, Audio.OsuAudioMod.None, DateTimeOffset.UtcNow);
        timeline.Start.Should().BeNull();
        timeline.End.Should().BeNull();
        using var json = JsonDocument.Parse(DiscordRichPresenceService.BuildActivityJson(new RichPresenceActivity("Song", "Artist", timeline.Start, null, timeline.End), 1, "n"));
        json.RootElement.GetProperty("args").GetProperty("activity").TryGetProperty("timestamps", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Service_HandshakesThenSendsActivity_AndReportsDiscordMissing()
    {
        var transport = new FakeTransport();
        await using var service = new DiscordRichPresenceService(_ => Task.FromResult<Stream?>(transport.ClientSide()));

        await service.ConfigureAsync(enabled: true);
        service.Status.Should().Contain("接続しました");
        await service.UpdateAsync(new RichPresenceActivity("Song", "▶ Artist", null, null));

        transport.Received.Should().HaveCount(2);
        transport.Received[0].Opcode.Should().Be(0);
        transport.Received[0].Json.Should().Contain("\"client_id\":\"1546645159406473246\"");
        transport.Received[1].Opcode.Should().Be(1);
        transport.Received[1].Json.Should().Contain("\"details\":\"Song\"");

        await service.UpdateAsync(null);
        using (var cleared = JsonDocument.Parse(transport.Received[^1].Json))
        {
            cleared.RootElement.GetProperty("args").TryGetProperty("activity", out _).Should().BeFalse();
        }

        await service.UpdateAsync(new RichPresenceActivity("Song", "▶ Artist", null, null));
        transport.Received[^1].Json.Should().Contain("\"details\":\"Song\"");

        await service.ConfigureAsync(enabled: false);
        service.Status.Should().Be("Rich Presence is off.");

        await using var missing = new DiscordRichPresenceService(_ => Task.FromResult<Stream?>(null));
        await missing.ConfigureAsync(enabled: true);
        missing.Status.Should().Contain("起動していません");
    }

    /// <summary>A duplex in-memory pipe: whatever the service writes is parsed and answered with a READY / OK frame.</summary>
    private sealed class FakeTransport
    {
        public List<(int Opcode, string Json)> Received { get; } = [];

        public Stream ClientSide() => new ReplyingStream(this);

        private sealed class ReplyingStream(FakeTransport owner) : Stream
        {
            private readonly MemoryStream incoming = new();
            private readonly MemoryStream replies = new();
            private long replyReadPosition;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count)
            {
                replies.Position = replyReadPosition;
                var read = replies.Read(buffer, offset, count);
                replyReadPosition = replies.Position;
                return read;
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count)
            {
                incoming.Write(buffer, offset, count);
                while (incoming.Length >= 8)
                {
                    var data = incoming.ToArray();
                    var length = BitConverter.ToInt32(data, 4);
                    if (data.Length < 8 + length)
                    {
                        return;
                    }

                    owner.Received.Add((BitConverter.ToInt32(data, 0), Encoding.UTF8.GetString(data, 8, length)));
                    incoming.SetLength(0);
                    incoming.Write(data, 8 + length, data.Length - 8 - length);
                    var reply = DiscordRichPresenceService.EncodeFrame(1, "{\"cmd\":\"DISPATCH\",\"evt\":\"READY\",\"data\":{}}");
                    replies.Position = replies.Length;
                    replies.Write(reply, 0, reply.Length);
                }
            }
        }
    }
}
