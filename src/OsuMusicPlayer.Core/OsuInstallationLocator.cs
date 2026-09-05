using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OsuMusicPlayer.Core;

public sealed class OsuInstallationLocator : IOsuInstallationLocator
{
    public IReadOnlyList<OsuInstallation> FindInstallations()
    {
        var installations = new List<OsuInstallation>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            addIfValid(installations, OsuInstallationKind.Stable, tryGetStableRegistryPath());
            addIfValid(installations, OsuInstallationKind.Stable, combineEnvironmentPath("LocalApplicationData", "osu!"));
            addIfValid(installations, OsuInstallationKind.Lazer, combineEnvironmentPath("ApplicationData", "osu"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            addIfValid(installations, OsuInstallationKind.Lazer, combineHomePath("Library", "Application Support", "osu"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            addIfValid(installations, OsuInstallationKind.Lazer, combineHomePath(".local", "share", "osu"));
            addIfValid(installations, OsuInstallationKind.Lazer, combineHomePath(".var", "app", "sh.ppy.osu", "data", "osu"));
        }

        return installations
            .DistinctBy(static installation => (installation.Kind, installation.RootPath), InstallationComparer.Instance)
            .ToArray();
    }

    public OsuInstallation? ValidateManualPath(string path, OsuInstallationKind kind)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (kind == OsuInstallationKind.Stable && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return null;
        }

        return isValid(fullPath, kind) ? new OsuInstallation(kind, fullPath) : null;
    }

    private static void addIfValid(ICollection<OsuInstallation> installations, OsuInstallationKind kind, string? path)
    {
        if (path is not null && isValid(path, kind))
        {
            installations.Add(new OsuInstallation(kind, Path.GetFullPath(path)));
        }
    }

    private static bool isValid(string path, OsuInstallationKind kind)
    {
        try
        {
            return Directory.Exists(path) && kind switch
            {
                OsuInstallationKind.Stable => File.Exists(Path.Combine(path, "osu!.db")) && Directory.Exists(Path.Combine(path, "Songs")),
                OsuInstallationKind.Lazer => File.Exists(Path.Combine(path, "client.realm")) && Directory.Exists(Path.Combine(path, "files")),
                _ => false,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static string? tryGetStableRegistryPath()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\osu!", writable: false);
            return key?.GetValue("") as string
                ?? key?.GetValue("path") as string;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static string? combineEnvironmentPath(string folderName, params string[] components)
    {
        if (!Enum.TryParse(folderName, out Environment.SpecialFolder folder))
        {
            return null;
        }

        var root = Environment.GetFolderPath(folder);
        return string.IsNullOrWhiteSpace(root) ? null : Path.Combine([root, .. components]);
    }

    private static string? combineHomePath(params string[] components)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(home) ? null : Path.Combine([home, .. components]);
    }

    private sealed class InstallationComparer : IEqualityComparer<(OsuInstallationKind Kind, string RootPath)>
    {
        public static InstallationComparer Instance { get; } = new();

        public bool Equals((OsuInstallationKind Kind, string RootPath) x, (OsuInstallationKind Kind, string RootPath) y) =>
            x.Kind == y.Kind && StringComparer.OrdinalIgnoreCase.Equals(x.RootPath, y.RootPath);

        public int GetHashCode((OsuInstallationKind Kind, string RootPath) obj) =>
            HashCode.Combine(obj.Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.RootPath));
    }
}
