using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OsuMusicPlayer.App.ViewModels;
using OsuMusicPlayer.Audio;

namespace OsuMusicPlayer.App;

/// <summary>Auto-play rendering of the selected difficulty, driven by the audio clock.</summary>
public sealed partial class DifficultyPreviewWindow : Window
{
    public DifficultyPreviewWindow()
    {
        InitializeComponent();
    }

    public DifficultyPreviewWindow(MainWindowViewModel viewModel, IAudioEngine audioEngine)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(audioEngine);
        DataContext = viewModel;
        Playfield.Clock = () => audioEngine.CurrentTime;
        FullscreenButton.Click += (_, _) => toggleFullscreen();
        CloseButton.Click += (_, _) => Close();
        AddHandler(KeyDownEvent, onKeyDown, RoutingStrategies.Tunnel);
    }

    private void toggleFullscreen() => WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;

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
