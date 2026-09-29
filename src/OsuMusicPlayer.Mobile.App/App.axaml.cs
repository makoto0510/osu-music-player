using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace OsuMusicPlayer.Mobile.App;

public partial class App : Application
{
    public static Func<IMobileAudioService>? AudioServiceFactory { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new MainView { DataContext = new MobileViewModel(AudioServiceFactory?.Invoke()) };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
