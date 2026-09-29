using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;

namespace OsuMusicPlayer.Mobile.Android;

[Activity(
    Label = "osu! ミュージックプレーヤー",
    Theme = "@style/MobileTheme.NoActionBar",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity<global::OsuMusicPlayer.Mobile.App.App>
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        var context = ApplicationContext ?? this;
        global::OsuMusicPlayer.Mobile.App.App.AudioServiceFactory = () => new AndroidMobileAudioService(context);
        base.OnCreate(savedInstanceState);
    }
}
