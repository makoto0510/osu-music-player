using OsuMusicPlayer.Audio;

namespace OsuMusicPlayer.App.ViewModels;

/// <summary>Entry points used by the music server bridge. Always called on the UI thread.</summary>
public sealed partial class MainWindowViewModel
{
    internal IReadOnlyList<TrackItemViewModel> LibrarySnapshot => allTracks.ToArray();

    internal TrackItemViewModel? FindTrack(Guid id) => tracksById.GetValueOrDefault(id);

    internal Task PlayTrackByIdAsync(Guid id) => FindTrack(id) is { } track ? playTrackAsync(track) : Task.CompletedTask;

    internal void PauseFromRemote()
    {
        if (audioEngine.State == AudioPlaybackState.Playing)
        {
            TogglePlayCommand.Execute(null);
        }
    }

    internal void ResumeFromRemote()
    {
        if (audioEngine.State != AudioPlaybackState.Playing)
        {
            TogglePlayCommand.Execute(null);
        }
    }

    internal void SeekSecondsFromRemote(double seconds)
    {
        var total = TotalTime.TotalSeconds;
        if (total > 0)
        {
            seek(Math.Clamp(seconds / total, 0, 1));
        }
    }

    internal bool EnqueueById(Guid id)
    {
        if (FindTrack(id) is not { } track)
        {
            return false;
        }

        Queue.Add(track);
        notifyQueueChanged();
        return true;
    }

    internal bool ToggleFavouriteById(Guid id)
    {
        if (FindTrack(id) is not { } track)
        {
            return false;
        }

        var previous = SelectedTrack;
        SelectedTrack = track;
        ToggleFavouriteCommand.Execute(null);
        SelectedTrack = previous;
        return true;
    }
}
