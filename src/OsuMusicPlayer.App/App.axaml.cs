using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using OsuMusicPlayer.Audio;
using OsuMusicPlayer.Core;
using OsuMusicPlayer.Core.Hitsounds;
using OsuMusicPlayer.Core.Loaders;
using OsuMusicPlayer.App.Services;
using OsuMusicPlayer.App.Themes;
using OsuMusicPlayer.App.ViewModels;
using OsuMusicPlayer.Server;

namespace OsuMusicPlayer.App;

public sealed partial class App : Application
{
    private static readonly TimeSpan shutdown_watchdog = TimeSpan.FromSeconds(8);

    private ServiceProvider? services;
    private ServerHostService? serverHost;
    private RichPresenceHostService? richPresenceHost;
    private ShutdownStage shutdownStage;

    private enum ShutdownStage
    {
        Running,
        CleaningUp,
        Done,
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            services = configureServices().BuildServiceProvider();
            desktop.MainWindow = services.GetRequiredService<MainWindow>();
            serverHost = services.GetRequiredService<ServerHostService>();
            serverHost.Attach();
            richPresenceHost = services.GetRequiredService<RichPresenceHostService>();
            richPresenceHost.Attach();
            desktop.ShutdownRequested += (_, args) => onShutdownRequested(desktop, args);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Closing the window used to block the UI thread while the HTTP server and the Discord
    /// pipe shut down, which left a busy cursor and, when a dispatcher call was pending,
    /// a hang. Now the first request is cancelled, playback is silenced and settings saved
    /// immediately, the services are disposed on a worker, and shutdown is requested again.
    /// The container holds async-only services, so it must be disposed with DisposeAsync;
    /// a synchronous Dispose throws and used to crash the process on the way out.
    /// </summary>
    private void onShutdownRequested(IClassicDesktopStyleApplicationLifetime desktop, ShutdownRequestedEventArgs args)
    {
        if (shutdownStage == ShutdownStage.Done || services is null)
        {
            return;
        }

        args.Cancel = true;
        if (shutdownStage == ShutdownStage.CleaningUp)
        {
            return;
        }

        shutdownStage = ShutdownStage.CleaningUp;
        services.GetRequiredService<MainWindowViewModel>().PrepareForShutdown();

        var watchdog = new Thread(() =>
        {
            Thread.Sleep(shutdown_watchdog);
            Environment.Exit(0);
        })
        {
            IsBackground = true,
            Name = "shutdown-watchdog",
        };
        watchdog.Start();

        var container = services;
        _ = Task.Run(async () =>
        {
            await disposeQuietlyAsync(richPresenceHost).ConfigureAwait(false);
            await disposeQuietlyAsync(serverHost).ConfigureAwait(false);
        }).ContinueWith(_ => Dispatcher.UIThread.Post(async () =>
        {
            // Everything left is synchronous (view model, audio engine, video player) and
            // belongs to the UI thread; the async hosts above are already disposed.
            await disposeQuietlyAsync(container).ConfigureAwait(true);
            shutdownStage = ShutdownStage.Done;
            desktop.Shutdown();
        }), TaskScheduler.Default);
    }

    private static async Task disposeQuietlyAsync(IAsyncDisposable? disposable)
    {
        if (disposable is null)
        {
            return;
        }

        try
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ObjectDisposedException or OperationCanceledException or System.Net.Sockets.SocketException or AggregateException)
        {
            // Shutdown must not fail because a remote service was already gone.
        }
    }

    private static IServiceCollection configureServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOsuInstallationLocator, OsuInstallationLocator>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>(static _ => new JsonSettingsStore());
        services.AddSingleton<IFolderPicker, AvaloniaFolderPicker>();
        services.AddSingleton<IFileSaver, AvaloniaFileSaver>();
        services.AddSingleton<IAudioDurationProbe, BassAudioDurationProbe>();
        services.AddSingleton<IDuplicateDetector, DuplicateDetector>();
        services.AddSingleton<IBeatmapLoader, OsuStableLoader>();
        services.AddSingleton<IBeatmapLoader, OsuLazerLoader>();
        services.AddSingleton<BeatmapManager>();
        services.AddSingleton<IBeatmapMediaResolver, BeatmapMediaResolver>();
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<IBackgroundImageLoader, BackgroundImageLoader>();
        services.AddSingleton<IAudioEngine>(static _ => AudioEngineFactory.Create());
        services.AddSingleton<IVideoPlayer>(static _ => VideoPlayerFactory.Create());
        services.AddSingleton<IHitsoundPlayer>(static provider => new BassHitsoundPlayer(provider.GetRequiredService<IAudioEngine>()));
        services.AddSingleton<IHitsoundSampleSourceFactory, HitsoundSampleSourceFactory>();
        services.AddSingleton<IStoryboardLoader>(static _ => new StoryboardLoader());
        services.AddSingleton<ILinkOpener, ShellLinkOpener>();
        services.AddSingleton<IThemeApplier, ApplicationThemeApplier>();
        services.AddSingleton<IOnlineMetadataService>(static _ => new OnlineMetadataService());
        services.AddSingleton<IPlayerBridge, ViewModelPlayerBridge>();
        services.AddSingleton<ServerHostService>();
        services.AddSingleton<IRichPresenceService, DiscordRichPresenceService>();
        services.AddSingleton<RichPresenceHostService>();
        services.AddSingleton<ICollectionLoader, OsuStableCollectionLoader>();
        services.AddSingleton<ICollectionLoader, OsuLazerCollectionLoader>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}
