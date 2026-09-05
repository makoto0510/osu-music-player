namespace OsuMusicPlayer.Core;

public interface IOsuInstallationLocator
{
    /// <summary>
    /// Finds osu! installations in their default locations for the current platform.
    /// </summary>
    IReadOnlyList<OsuInstallation> FindInstallations();

    /// <summary>
    /// Validates a user-selected folder and returns the installation it describes, or
    /// <see langword="null"/> when the folder does not contain the expected osu! data.
    /// </summary>
    OsuInstallation? ValidateManualPath(string path, OsuInstallationKind kind);
}
