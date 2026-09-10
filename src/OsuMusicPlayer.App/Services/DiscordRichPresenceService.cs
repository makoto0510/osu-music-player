using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OsuMusicPlayer.App.Services;

/// <summary>What Discord shows for the player.</summary>
public sealed record RichPresenceActivity(string Details, string State, DateTimeOffset? StartedAt, string? LargeText, DateTimeOffset? EndsAt = null, string? LargeImage = null);

public interface IRichPresenceService : IAsyncDisposable
{
    /// <summary>Human-readable connection state for the settings panel.</summary>
    string Status { get; }

    event EventHandler? StatusChanged;

    /// <summary>Connects with the built-in application id, or disconnects and clears presence when disabled.</summary>
    Task ConfigureAsync(bool enabled, CancellationToken cancellationToken = default);

    /// <summary>Publishes the activity, or clears it when <paramref name="activity"/> is null.</summary>
    Task UpdateAsync(RichPresenceActivity? activity, CancellationToken cancellationToken = default);
}

/// <summary>
/// Discord Rich Presence over the local IPC socket (no third-party SDK). The protocol is
/// tiny: length-prefixed JSON frames, a handshake with the application id, then
/// SET_ACTIVITY commands. Discord not running is a normal, quietly reported state.
/// </summary>
public sealed class DiscordRichPresenceService : IRichPresenceService
{
    private const int op_handshake = 0;
    private const int op_frame = 1;
    private const int op_close = 2;
    private const int max_frame = 64 * 1024;

    private static readonly JsonSerializerOptions json_options = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly Func<CancellationToken, Task<Stream?>> connect;
    private readonly SemaphoreSlim gate = new(1, 1);
    private Stream? stream;
    private const string applicationId = "1546645159406473246";
    private bool enabled;
    private RichPresenceActivity? lastActivity;
    private string status = "Rich Presence is off.";

    public DiscordRichPresenceService()
        : this(connectToDiscordAsync)
    {
    }

    /// <summary>Lets tests replace the IPC transport.</summary>
    internal DiscordRichPresenceService(Func<CancellationToken, Task<Stream?>> connect)
    {
        this.connect = connect ?? throw new ArgumentNullException(nameof(connect));
    }

    public string Status
    {
        get => status;
        private set
        {
            if (status != value)
            {
                status = value;
                StatusChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler? StatusChanged;

    public async Task ConfigureAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var changed = this.enabled != enabled;
            this.enabled = enabled;
            if (!enabled)
            {
                await disconnectAsync().ConfigureAwait(false);
                Status = "Rich Presence is off.";
                return;
            }

            if (changed)
            {
                await disconnectAsync().ConfigureAwait(false);
            }

            await ensureConnectedAsync(cancellationToken).ConfigureAwait(false);
            if (stream is not null && lastActivity is not null)
            {
                await sendActivityAsync(lastActivity, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task UpdateAsync(RichPresenceActivity? activity, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lastActivity = activity;
            if (!enabled)
            {
                return;
            }

            await ensureConnectedAsync(cancellationToken).ConfigureAwait(false);
            if (stream is not null)
            {
                await sendActivityAsync(activity, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await disconnectAsync().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
            gate.Dispose();
        }
    }

    /// <summary>Builds the SET_ACTIVITY payload Discord expects.</summary>
    internal static string BuildActivityJson(RichPresenceActivity? activity, int processId, string nonce)
    {
        var payload = new
        {
            cmd = "SET_ACTIVITY",
            args = new
            {
                pid = processId,
                activity = activity is null ? null : new
                {
                    type = 2,
                    details = truncate(activity.Details),
                    state = truncate(activity.State),
                    timestamps = activity.StartedAt is { } started ? new { start = started.ToUnixTimeSeconds(), end = activity.EndsAt?.ToUnixTimeSeconds() } : null,
                    assets = activity.LargeText is null && activity.LargeImage is null ? null : new
                    {
                        large_image = activity.LargeImage,
                        large_text = activity.LargeText is null ? null : truncate(activity.LargeText),
                    },
                },
            },
            nonce,
        };
        return JsonSerializer.Serialize(payload, json_options);
    }

    internal static byte[] EncodeFrame(int opcode, string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var frame = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), body.Length);
        body.CopyTo(frame, 8);
        return frame;
    }

    internal static async Task<(int Opcode, string Json)?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[8];
        if (!await readExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var opcode = BinaryPrimitives.ReadInt32LittleEndian(header);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (length < 0 || length > max_frame)
        {
            return null;
        }

        var body = new byte[length];
        if (!await readExactlyAsync(stream, body, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return (opcode, Encoding.UTF8.GetString(body));
    }

    private async Task ensureConnectedAsync(CancellationToken cancellationToken)
    {
        if (stream is not null)
        {
            return;
        }

        Stream? connection = null;
        try
        {
            connection = await connect(cancellationToken).ConfigureAwait(false);
            if (connection is null)
            {
                Status = "Discord が起動していません。起動すると自動的に接続します。";
                return;
            }

            var handshake = JsonSerializer.Serialize(new { v = 1, client_id = applicationId }, json_options);
            await connection.WriteAsync(EncodeFrame(op_handshake, handshake), cancellationToken).ConfigureAwait(false);
            await connection.FlushAsync(cancellationToken).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var reply = await ReadFrameAsync(connection, timeout.Token).ConfigureAwait(false);
            if (reply is null || reply.Value.Opcode == op_close || reply.Value.Json.Contains("\"code\"", StringComparison.Ordinal) && reply.Value.Json.Contains("Invalid", StringComparison.OrdinalIgnoreCase))
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                Status = "Discord が接続を拒否しました。Discord を再起動して再度有効にしてください。";
                return;
            }

            stream = connection;
            Status = "Discord に接続しました。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException or OperationCanceledException or SocketException or JsonException)
        {
            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }

            Status = exception is OperationCanceledException && !cancellationToken.IsCancellationRequested
                ? "Discord が応答しません。"
                : $"Discord に接続できませんでした: {exception.Message}";
        }
    }

    private async Task sendActivityAsync(RichPresenceActivity? activity, CancellationToken cancellationToken)
    {
        if (stream is null)
        {
            return;
        }

        try
        {
            var json = BuildActivityJson(activity, Environment.ProcessId, Guid.NewGuid().ToString());
            await stream.WriteAsync(EncodeFrame(op_frame, json), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var reply = await ReadFrameAsync(stream, timeout.Token).ConfigureAwait(false);
            if (reply is null || reply.Value.Opcode == op_close)
            {
                await disconnectAsync().ConfigureAwait(false);
                Status = "Discord との接続が切れました。次回の更新で再接続します。";
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException or SocketException)
        {
            await disconnectAsync().ConfigureAwait(false);
            Status = "Discord との接続が切れました。次回の更新で再接続します。";
        }
    }

    private async Task disconnectAsync()
    {
        if (stream is not { } current)
        {
            return;
        }

        stream = null;
        try
        {
            await current.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }
    }

    private static async Task<bool> readExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private static async Task<Stream?> connectToDiscordAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < 10; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OperatingSystem.IsWindows())
            {
                var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
                try
                {
                    await pipe.ConnectAsync(500, cancellationToken).ConfigureAwait(false);
                    return pipe;
                }
                catch (Exception exception) when (exception is TimeoutException or IOException or UnauthorizedAccessException)
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                }
            }
            else
            {
                foreach (var directory in new[] { Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"), Environment.GetEnvironmentVariable("TMPDIR"), "/tmp" })
                {
                    if (string.IsNullOrEmpty(directory))
                    {
                        continue;
                    }

                    var path = Path.Combine(directory, $"discord-ipc-{i}");
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try
                    {
                        await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), cancellationToken).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (SocketException)
                    {
                        socket.Dispose();
                    }
                }
            }
        }

        return null;
    }

    private static string truncate(string value) => value.Length <= 120 ? value : value[..117] + "…";
}
