using ManagedBass;
using ManagedBass.Fx;

namespace OsuMusicPlayer.Audio;

internal interface IBassNative
{
    Errors LastError { get; }

    bool Init();

    bool Free();

    int CreateDecodeStream(string path);

    int CreateTempoStream(int sourceStream);

    bool FreeStream(int stream);

    bool Play(int stream, bool restart);

    bool Pause(int stream);

    bool Stop(int stream);

    PlaybackState GetState(int stream);

    bool SetAttribute(int stream, ChannelAttribute attribute, float value);

    long GetLength(int stream);

    long GetPosition(int stream);

    double BytesToSeconds(int stream, long position);

    long SecondsToBytes(int stream, double seconds);

    bool SetPosition(int stream, long position);

    int SetEndSync(int stream, SyncProcedure procedure, nint user);
}

internal sealed class BassNative : IBassNative
{
    public Errors LastError => Bass.LastError;

    public bool Init() => Bass.Init(-1, 44_100, DeviceInitFlags.Default, nint.Zero, nint.Zero);

    public bool Free() => Bass.Free();

    public int CreateDecodeStream(string path) =>
        Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Prescan);

    public int CreateTempoStream(int sourceStream) =>
        BassFx.TempoCreate(sourceStream, BassFlags.FxFreeSource);

    public bool FreeStream(int stream) => Bass.StreamFree(stream);

    public bool Play(int stream, bool restart) => Bass.ChannelPlay(stream, restart);

    public bool Pause(int stream) => Bass.ChannelPause(stream);

    public bool Stop(int stream) => Bass.ChannelStop(stream);

    public PlaybackState GetState(int stream) => Bass.ChannelIsActive(stream);

    public bool SetAttribute(int stream, ChannelAttribute attribute, float value) =>
        Bass.ChannelSetAttribute(stream, attribute, value);

    public long GetLength(int stream) => Bass.ChannelGetLength(stream, PositionFlags.Bytes);

    public long GetPosition(int stream) => Bass.ChannelGetPosition(stream, PositionFlags.Bytes);

    public double BytesToSeconds(int stream, long position) => Bass.ChannelBytes2Seconds(stream, position);

    public long SecondsToBytes(int stream, double seconds) => Bass.ChannelSeconds2Bytes(stream, seconds);

    public bool SetPosition(int stream, long position) => Bass.ChannelSetPosition(stream, position, PositionFlags.Bytes);

    public int SetEndSync(int stream, SyncProcedure procedure, nint user) =>
        Bass.ChannelSetSync(stream, SyncFlags.End | SyncFlags.Onetime, 0, procedure, user);
}
