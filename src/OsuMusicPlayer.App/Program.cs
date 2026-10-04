using Avalonia;
using Avalonia.Media;
using OsuMusicPlayer.App.Themes;

namespace OsuMusicPlayer.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = UiCustomization.LatinFontFamily,
                FontFallbacks =
                [
                    new FontFallback { FontFamily = new FontFamily(UiCustomization.JapaneseFontFamily) },
                ],
            })
            .LogToTrace();
}
