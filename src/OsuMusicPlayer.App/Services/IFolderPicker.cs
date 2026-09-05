namespace OsuMusicPlayer.App.Services;

public interface IFolderPicker
{
    /// <summary>
    /// Shows a folder picker and returns the selected local path, or
    /// <see langword="null"/> when the user cancelled or no picker is available.
    /// </summary>
    Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default);
}
