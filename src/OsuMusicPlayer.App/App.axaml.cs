using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using OsuMusicPlayer.Audio;
using OsuMusicPlayer.Core;
using OsuMusicPlayer.Core.Hitsounds;
using OsuMusicPlayer.Core.Loaders;
using OsuMusicPlayer.App.Services;
using OsuMusicPlayer.App.ViewModels;

namespace OsuMusicPlayer.App;

public sealed partial class App : Application
{
    private ServiceProvider? services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            services = configureServices().BuildServiceProvider();
            desktop.MainWindow = services.GetRequiredService<MainWindow>();
            desktop.Exit += (_, _) => services.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceCollection configureServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOsuInstallationLocator, OsuInstallationLocator>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>(static _ => new JsonSettingsStore());
        services.AddSingleton<IFolderPicker, AvaloniaFolderPicker>();
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
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}
