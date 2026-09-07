using OsuMusicPlayer.App.ViewModels;
using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Server;

namespace OsuMusicPlayer.App.Services;

/// <summary>Exposes the main view model to the music server, marshalling every call to the UI thread.</summary>
public sealed class ViewModelPlayerBridge(MainWindowViewModel viewModel, IUiDispatcher dispatcher) : IPlayerBridge
{
    private readonly MainWindowViewModel viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    private readonly IUiDispatcher dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    public Task<IReadOnlyList<ServerTrack>> GetTracksAsync(CancellationToken cancellationToken) =>
        onUi(() => (IReadOnlyList<ServerTrack>)viewModel.LibrarySnapshot.Select(toServerTrack).ToArray());

    public Task<ServerTrack?> GetTrackAsync(Guid id, CancellationToken cancellationToken) =>
        onUi(() => viewModel.FindTrack(id) is { } track ? toServerTrack(track) : null);

    public Task<PlayerState> GetStateAsync(CancellationToken cancellationToken) =>
        onUi(() => new PlayerState(
            viewModel.CurrentTrack is { } current ? toServerTrack(current) : null,
            viewModel.IsPlaying,
            viewModel.CurrentTime.TotalSeconds,
            viewModel.TotalTime.TotalSeconds,
            viewModel.MasterVolume,
            viewModel.Mod.ToString(),
            viewModel.IsShuffleEnabled,
            viewModel.RepeatMode.ToString(),
            viewModel.Queue.Select(toServerTrack).ToArray()));

    public async Task<bool> PlayAsync(Guid id, CancellationToken cancellationToken)
    {
        Task? play = null;
        var found = await onUi(() =>
        {
            if (viewModel.FindTrack(id) is null)
            {
                return false;
            }

            play = viewModel.PlayTrackByIdAsync(id);
            return true;
        }).ConfigureAwait(false);
        if (play is not null)
        {
            await play.ConfigureAwait(false);
        }

        return found;
    }

    public Task TogglePlayAsync(CancellationToken cancellationToken) => dispatcher.InvokeAsync(() => viewModel.TogglePlayCommand.Execute(null));

    public Task PauseAsync(CancellationToken cancellationToken) => dispatcher.InvokeAsync(viewModel.PauseFromRemote);

    public Task ResumeAsync(CancellationToken cancellationToken) => dispatcher.InvokeAsync(viewModel.ResumeFromRemote);

    public async Task NextAsync(CancellationToken cancellationToken)
    {
        Task? next = null;
        await dispatcher.InvokeAsync(() => next = viewModel.NextCommand.ExecuteAsync(null)).ConfigureAwait(false);
        if (next is not null)
        {
            await next.ConfigureAwait(false);
        }
    }

    public async Task PreviousAsync(CancellationToken cancellationToken)
    {
        Task? previous = null;
        await dispatcher.InvokeAsync(() => previous = viewModel.PreviousCommand.ExecuteAsync(null)).ConfigureAwait(false);
        if (previous is not null)
        {
            await previous.ConfigureAwait(false);
        }
    }

    public Task SeekAsync(double seconds, CancellationToken cancellationToken) => dispatcher.InvokeAsync(() => viewModel.SeekSecondsFromRemote(seconds));

    public Task SetVolumeAsync(double volume, CancellationToken cancellationToken) => dispatcher.InvokeAsync(() => viewModel.MasterVolume = Math.Clamp(volume, 0, 1));

    public Task<bool> EnqueueAsync(Guid id, CancellationToken cancellationToken) => onUi(() => viewModel.EnqueueById(id));

    public Task<bool> ToggleFavouriteAsync(Guid id, CancellationToken cancellationToken) => onUi(() => viewModel.ToggleFavouriteById(id));

    private async Task<T> onUi<T>(Func<T> read)
    {
        T result = default!;
        await dispatcher.InvokeAsync(() => result = read()).ConfigureAwait(false);
        return result;
    }

    private static ServerTrack toServerTrack(TrackItemViewModel track) => new(
        track.Model.Id,
        track.Title,
        track.Artist,
        track.Creator,
        track.BPM,
        track.Length.TotalSeconds,
        track.Model.OnlineId,
        track.IsFavourite,
        track.Model.Source switch
        {
            BeatmapSource.Stable => "stable",
            BeatmapSource.Lazer => "lazer",
            _ => "both",
        },
        track.Model.BackgroundFilePath is not null)
    {
        TitleRomanised = track.Model.Title,
        ArtistRomanised = track.Model.Artist,
        AudioPath = track.Model.AudioFilePath,
        BackgroundPath = track.Model.BackgroundFilePath,
    };
}
