using System.ComponentModel;
using OsuMusicPlayer.App.ViewModels;
using OsuMusicPlayer.Server;

namespace OsuMusicPlayer.App.Services;

/// <summary>Starts and stops the music server as the settings toggles change and reports its addresses.</summary>
public sealed class ServerHostService : IAsyncDisposable
{
    private readonly MainWindowViewModel viewModel;
    private readonly IPlayerBridge bridge;
    private readonly IUiDispatcher dispatcher;
    private readonly SemaphoreSlim gate = new(1, 1);
    private MusicServer? server;
    private bool attached;
    private bool disposed;

    public ServerHostService(MainWindowViewModel viewModel, IPlayerBridge bridge, IUiDispatcher dispatcher)
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public bool IsRunning => server?.IsRunning == true;

    public void Attach()
    {
        if (attached)
        {
            return;
        }

        attached = true;
        viewModel.PropertyChanged += onViewModelPropertyChanged;
        _ = applyAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        viewModel.PropertyChanged -= onViewModelPropertyChanged;
        await stopAsync().ConfigureAwait(false);
        gate.Dispose();
    }

    private void onViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainWindowViewModel.IsServerEnabled) or nameof(MainWindowViewModel.ServerPort) or nameof(MainWindowViewModel.ServerAllowRemote))
        {
            _ = applyAsync();
        }
    }

    private async Task applyAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed)
            {
                return;
            }

            bool enabled = false;
            int port = 5150;
            bool allowRemote = true;
            await dispatcher.InvokeAsync(() =>
            {
                enabled = viewModel.IsServerEnabled;
                port = viewModel.ServerPort;
                allowRemote = viewModel.ServerAllowRemote;
            }).ConfigureAwait(false);

            await stopCoreAsync().ConfigureAwait(false);
            if (!enabled)
            {
                await setStatusAsync("Server is off.").ConfigureAwait(false);
                return;
            }

            var candidate = new MusicServer();
            try
            {
                await candidate.StartAsync(new MusicServerOptions { Port = port, AllowRemoteConnections = allowRemote }, bridge).ConfigureAwait(false);
                server = candidate;
                var urls = string.Join("  ", candidate.Urls);
                await setStatusAsync($"Listening: {urls}  ·  overlay: {candidate.Urls[0]}overlay").ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or System.Net.Sockets.SocketException or UnauthorizedAccessException)
            {
                await candidate.DisposeAsync().ConfigureAwait(false);
                await setStatusAsync($"サーバーを起動できませんでした: {exception.Message}").ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task stopAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await stopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task stopCoreAsync()
    {
        if (server is { } running)
        {
            server = null;
            await running.DisposeAsync().ConfigureAwait(false);
        }
    }

    private Task setStatusAsync(string text) => dispatcher.InvokeAsync(() => viewModel.ServerStatusText = text);
}
