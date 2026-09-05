namespace OsuMusicPlayer.Audio;

public sealed class AudioEngineException : Exception
{
    public AudioEngineException(string message)
        : base(message)
    {
    }

    public AudioEngineException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
