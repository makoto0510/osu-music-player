using System.Diagnostics;
using System.Runtime.InteropServices;

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

    public bool OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var start = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? new ProcessStartInfo("explorer.exe", $"\"{fullPath}\"")
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? new ProcessStartInfo("open", $"\"{fullPath}\"")
                : new ProcessStartInfo("xdg-open", $"\"{fullPath}\"");
            start.UseShellExecute = false;
            using var process = Process.Start(start);
            return process is not null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException or IOException)
        {
            return false;
        }
    }
}
