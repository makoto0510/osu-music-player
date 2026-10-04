using OsuMusicPlayer.Audio;
using OsuMusicPlayer.Core.Search;
using OsuMusicPlayer.Server;

namespace OsuMusicPlayer.App.ViewModels;

/// <summary>Entry points used by the music server bridge. Always called on the UI thread.</summary>
public sealed partial class MainWindowViewModel
{
    internal IReadOnlyList<TrackItemViewModel> LibrarySnapshot => allTracks.ToArray();

    internal IReadOnlyList<ServerPlaylist> PlaylistSnapshot => Playlists.Select(playlist =>
        new ServerPlaylist(playlist.Id, playlist.Name, playlist.TrackIds.Where(tracksById.ContainsKey).ToArray()))
        .Concat(SmartPlaylists.Select(playlist =>
        {
            var query = TrackSearchQuery.Parse(playlist.Query);
            return new ServerPlaylist(playlist.Id, playlist.Name,
                allTracks.Where(track => query.Matches(track.Model, track.Genre, track.Language))
                    .Select(track => track.Model.Id).ToArray(), IsSmart: true);
        })).ToArray();

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
