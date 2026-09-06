using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LibVLCSharp.Shared;
using OsuMusicPlayer.App.ViewModels;
using OsuMusicPlayer.Audio;

namespace OsuMusicPlayer.App;

/// <summary>
/// The video and storyboard in their own window, optionally fullscreen. While it is open
/// the main window gives up the libVLC surface (there is only one), so the video plays here.
/// </summary>
public sealed partial class VisualsWindow : Window
{
    private readonly MediaPlayer? videoSurface;

    public VisualsWindow()
    {
        InitializeComponent();
    }

    public VisualsWindow(MainWindowViewModel viewModel, MediaPlayer? videoSurface, IAudioEngine audioEngine)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(audioEngine);
        this.videoSurface = videoSurface;
        DataContext = viewModel;
        StoryboardView.Clock = () => audioEngine.CurrentTime;

        VideoView.AttachedToVisualTree += (_, _) =>
        {
            if (videoSurface is not null)
            {
                VideoView.MediaPlayer = null;
                VideoView.MediaPlayer = videoSurface;
                viewModel.RestartVideoSurface(); // libVLC needs a restart to draw into the new handle
            }
        };

        FullscreenButton.Click += (_, _) => toggleFullscreen();
        CloseButton.Click += (_, _) => Close();
        DoubleTapped += (_, _) => toggleFullscreen();
        AddHandler(KeyDownEvent, onKeyDown, RoutingStrategies.Tunnel);
        Closing += (_, _) =>
        {
            if (videoSurface is not null)
            {
                VideoView.MediaPlayer = null;
            }
        };
    }

    private void toggleFullscreen()
    {
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
        Toolbar.IsVisible = WindowState != WindowState.FullScreen;
    }

    private void onKeyDown(object? sender, KeyEventArgs args)
    {
        switch (args.Key)
        {
            case Key.F11:
                toggleFullscreen();
                args.Handled = true;
                break;
            case Key.Escape:
                if (WindowState == WindowState.FullScreen)
                {
                    toggleFullscreen();
                }
                else
                {
                    Close();
                }

                args.Handled = true;
                break;
            case Key.Space when DataContext is MainWindowViewModel viewModel:
                viewModel.TogglePlayCommand.Execute(null);
                args.Handled = true;
                break;
        }
    }
}
