using Avalonia.Controls;
using LibVLCSharp.Shared;
using OsuMusicPlayer.App.Services;
using OsuMusicPlayer.App.ViewModels;
using OsuMusicPlayer.Audio;

namespace OsuMusicPlayer.App;

public sealed partial class MainWindow : Window
{
    private readonly MediaPlayer? videoSurface;
    private bool initialized;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel, IVideoPlayer videoPlayer, IAudioEngine audioEngine)
        : this()
    {
        ArgumentNullException.ThrowIfNull(videoPlayer);
        ArgumentNullException.ThrowIfNull(audioEngine);
        DataContext = viewModel;
        videoSurface = videoPlayer.Surface as MediaPlayer;

        // The storyboard follows the audio engine's clock directly so animation stays
        // smooth; the view model's 100 ms position updates are too coarse for that.
        StoryboardView.Clock = () => audioEngine.CurrentTime;

        // LibVLCSharp.Avalonia only hands the native window handle to libVLC inside the
        // MediaPlayer setter, and the handle does not exist until the view is attached to
        // the visual tree. Assigning here (before the window is shown) would leave libVLC
        // without a handle and it would open its own window, so assign on every attach.
        VideoView.AttachedToVisualTree += (_, _) => attachVideoSurface();

        Opened += async (_, _) =>
        {
            if (!initialized)
            {
                initialized = true;
                await viewModel.InitializeAsync();
            }
        };
    }

    private void attachVideoSurface()
    {
        if (videoSurface is null)
        {
            return;
        }

        // Re-assigning forces the view to detach and attach again with the current handle.
        VideoView.MediaPlayer = null;
        VideoView.MediaPlayer = videoSurface;
    }
}
