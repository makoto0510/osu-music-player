using CommunityToolkit.Mvvm.ComponentModel;
using OsuMusicPlayer.Core.Preview;

namespace OsuMusicPlayer.App.ViewModels;

/// <summary>Loads the hit objects of the selected difficulty for the gameplay preview popup.</summary>
public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDifficultyPreview))]
    private PlayfieldPreviewData? difficultyPreview;

    [ObservableProperty]
    private string difficultyPreviewTitle = string.Empty;

    [ObservableProperty]
    private string difficultyPreviewStatusText = string.Empty;

    public bool HasDifficultyPreview => DifficultyPreview is not null;

    /// <summary>
    /// Prepares the preview for the difficulty selected in the details pane: starts that track
    /// if it is not already playing (so the preview follows the audio clock) and parses the
    /// .osu file. Returns false when there is nothing to preview.
    /// </summary>
    public async Task<bool> OpenDifficultyPreviewAsync()
    {
        var track = DetailTrack;
        var difficulty = track?.SelectedDifficulty;
        if (track is null || difficulty is null || string.IsNullOrEmpty(difficulty.Model.BeatmapFilePath))
        {
            DifficultyPreviewStatusText = "Select a difficulty with a .osu file to preview it.";
            return false;
        }

        DifficultyPreviewTitle = $"{track.Artist} - {track.Title} [{difficulty.Name}]";
        DifficultyPreviewStatusText = "Loading…";
        var path = difficulty.Model.BeatmapFilePath;
        PlayfieldPreviewData data;
        try
        {
            data = await Task.Run(() => PlayfieldPreviewBuilder.Build(path)).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException or ArgumentException)
        {
            DifficultyPreviewStatusText = $"Could not read the difficulty: {exception.Message}";
            return false;
        }

        DifficultyPreview = data;
        DifficultyPreviewStatusText = data.Objects.Count == 0 ? "This difficulty has no hit objects." : string.Empty;
        if (CurrentTrack != track)
        {
            await playTrackAsync(track).ConfigureAwait(true);
        }
        else if (!IsPlaying)
        {
            TogglePlayCommand.Execute(null);
        }

        return true;
    }

    public void CloseDifficultyPreview()
    {
        DifficultyPreview = null;
        DifficultyPreviewStatusText = string.Empty;
    }
}
