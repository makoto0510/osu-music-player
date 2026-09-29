using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using OsuMusicPlayer.Mobile.App;

namespace OsuMusicPlayer.Mobile.Android;

[Application]
public sealed class Application : AvaloniaAndroidApplication<App>
{
    public Application(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder);
}
