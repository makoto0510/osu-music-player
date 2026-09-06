using System.ComponentModel;
using OsuMusicPlayer.App.ViewModels;

namespace OsuMusicPlayer.App.Services;

/// <summary>Feeds the current track into Discord Rich Presence and mirrors the connection status into the view model.</summary>
public sealed class RichPresenceHostService : IAsyncDisposable
{
    private readonly MainWindowViewModel viewModel;
    private readonly IRichPresenceService presence;
    private readonly IUiDispatcher dispatcher;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool attached;
    private bool disposed;

    public RichPresenceHostService(MainWindowViewModel viewModel, IRichPresenceService presence, IUiDispatcher dispatcher)
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.presence = presence ?? throw new ArgumentNullException(nameof(presence));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public void Attach()
    {
        if (attached)
        {
            return;
        }

        attached = true;
        viewModel.PropertyChanged += onViewModelPropertyChanged;
        presence.StatusChanged += onStatusChanged;
        _ = configureAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        viewModel.PropertyChanged -= onViewModelPropertyChanged;
        presence.StatusChanged -= onStatusChanged;
        try
        {
            await presence.UpdateAsync(null).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }

        await presence.DisposeAsync().ConfigureAwait(false);
        gate.Dispose();
    }

    private void onViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(MainWindowViewModel.IsRichPresenceEnabled):
            case nameof(MainWindowViewModel.DiscordApplicationId):
                _ = configureAsync();
                break;
            case nameof(MainWindowViewModel.CurrentTrack):
            case nameof(MainWindowViewModel.IsPlaying):
            case nameof(MainWindowViewModel.Mod):
                _ = updateAsync();
                break;
        }
    }

    private void onStatusChanged(object? sender, EventArgs args) =>
        _ = dispatcher.InvokeAsync(() => viewModel.RichPresenceStatusText = presence.Status);

    private async Task configureAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed)
            {
                return;
            }

            string id = string.Empty;
            bool enabled = false;
            await dispatcher.InvokeAsync(() =>
            {
                id = viewModel.DiscordApplicationId;
                enabled = viewModel.IsRichPresenceEnabled;
            }).ConfigureAwait(false);
            await presence.ConfigureAsync(id, enabled).ConfigureAwait(false);
            await presence.UpdateAsync(await buildActivityAsync().ConfigureAwait(false)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task updateAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!disposed)
            {
                await presence.UpdateAsync(await buildActivityAsync().ConfigureAwait(false)).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<RichPresenceActivity?> buildActivityAsync()
    {
        RichPresenceActivity? activity = null;
        await dispatcher.InvokeAsync(() =>
        {
            if (viewModel.CurrentTrack is not { } track)
            {
                return;
            }

            var mod = viewModel.Mod == Audio.OsuAudioMod.None ? string.Empty : $" [{viewModel.Mod}]";
            var elapsed = viewModel.IsPlaying ? viewModel.CurrentTime : (TimeSpan?)null;
            activity = new RichPresenceActivity(
                track.Title,
                (viewModel.IsPlaying ? "▶ " : "⏸ ") + track.Artist + mod,
                elapsed is { } time ? DateTimeOffset.UtcNow - time : null,
                $"mapped by {track.Creator}");
        }).ConfigureAwait(false);
        return activity;
    }
}
