using System.Diagnostics;

namespace OsuMusicPlayer.App.Services;

public interface ILinkOpener
{
    /// <summary>Opens an http(s) link in the user's browser. Returns false when nothing could be launched.</summary>
    bool Open(Uri uri);
}

public sealed class ShellLinkOpener : ILinkOpener
{
    public bool Open(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
            return process is not null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
