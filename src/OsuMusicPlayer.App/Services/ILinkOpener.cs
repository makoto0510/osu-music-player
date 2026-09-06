using System.Diagnostics;

namespace OsuMusicPlayer.App.Services;

public interface ILinkOpener
{
    /// <summary>Opens an http(s) link in the user's browser. Returns false when nothing could be launched.</summary>
    bool Open(Uri uri);

    /// <summary>Shows a local folder in the platform's file manager. Returns false when it does not exist or nothing could be launched.</summary>
    bool OpenFolder(string path);
}

public sealed class ShellLinkOpener : ILinkOpener
{
    public bool Open(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.Scheme is "http" or "https" && start(uri.ToString());
    }

    public bool OpenFolder(string path) => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && start(Path.GetFullPath(path));

    /// <summary>Hands the target to the OS shell (browser for URLs, file manager for folders); false when nothing could be launched.</summary>
    private static bool start(string target)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return process is not null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException or IOException)
        {
            return false;
        }
    }
}
