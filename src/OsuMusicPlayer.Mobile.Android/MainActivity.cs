using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace OsuMusicPlayer.Mobile.Android;

[Activity(
    Label = "osu! music player",
    Theme = "@style/MobileTheme.NoActionBar",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity
{
}
