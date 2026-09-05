using ManagedBass;
using OsuMusicPlayer.Audio;

namespace OsuMusicPlayer.Audio.Tests;

internal sealed class FakeBassNative : IBassNative
{
    private int nextStream = 10;

    public Errors LastError { get; set; }
    public bool InitResult { get; set; } = true;
    public bool FreeResult { get; set; } = true;
    public bool FailDecode { get; set; }
    public bool FailSync { get; set; }
    public bool FailTempo { get; set; }
    public Exception? TempoError { get; set; }
    public bool InitCalled { get; private set; }
    public bool FreeCalled { get; private set; }
    public PlaybackState State { get; set; }
    public long Length { get; set; } = 120_000;
    public long Position { get; set; }
    public List<int> FreedStreams { get; } = [];
    public List<(int Stream, ChannelAttribute Attribute, float Value)> Attributes { get; } = [];
    public SyncProcedure? EndProcedure { get; private set; }
    public nint EndUser { get; private set; }
    public int CurrentTempoStream { get; private set; }
    public int LastDecodeStream { get; private set; }

    public bool Init() { InitCalled = true; return InitResult; }
    public bool Free() { FreeCalled = true; return FreeResult; }
    public int CreateDecodeStream(string path) => FailDecode ? 0 : LastDecodeStream = nextStream++;
    public int CreateTempoStream(int sourceStream)
    {
        if (TempoError is not null)
        {
            throw TempoError;
        }

        return FailTempo ? 0 : CurrentTempoStream = nextStream++;
    }
    public bool FreeStream(int stream) { FreedStreams.Add(stream); return true; }
    public bool Play(int stream, bool restart) { State = PlaybackState.Playing; return true; }
    public bool Pause(int stream) { State = PlaybackState.Paused; return true; }
    public bool Stop(int stream) { State = PlaybackState.Stopped; return true; }
    public PlaybackState GetState(int stream) => State;
    public bool SetAttribute(int stream, ChannelAttribute attribute, float value) { Attributes.Add((stream, attribute, value)); return true; }
    public long GetLength(int stream) => Length;
    public long GetPosition(int stream) => Position;
    public double BytesToSeconds(int stream, long position) => position / 1000d;
    public long SecondsToBytes(int stream, double seconds) => (long)(seconds * 1000);
    public bool SetPosition(int stream, long position) { Position = position; return true; }
    public int SetEndSync(int stream, SyncProcedure procedure, nint user)
    {
        EndProcedure = procedure;
        EndUser = user;
        return FailSync ? 0 : 1;
    }

    public void RaiseEnded() => EndProcedure?.Invoke(1, CurrentTempoStream, 0, EndUser);
}
