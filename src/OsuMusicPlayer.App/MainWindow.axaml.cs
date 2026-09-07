using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using OsuMusicPlayer.App.Input;
using OsuMusicPlayer.App.Services;
using OsuMusicPlayer.App.ViewModels;
using OsuMusicPlayer.Audio;

namespace OsuMusicPlayer.App;

public sealed partial class MainWindow : Window
{
    private readonly MainWindowViewModel? viewModel;
    private readonly MediaPlayer? videoSurface;
    private readonly IAudioEngine? audioEngine;
    private VisualsWindow? visualsWindow;
    private DifficultyPreviewWindow? previewWindow;
    private bool initialized;

    public MainWindow()
    {
        // The XAML binds to these through #Root, and plain CLR properties are read once when
        // the window is built, so they must exist before InitializeComponent runs.
        ToggleVisualsWindowCommand = new RelayCommand(() => toggleVisualsWindow(fullscreen: false));
        FullscreenVisualsCommand = new RelayCommand(() => openVisualsWindow(fullscreen: true));
        OpenDifficultyPreviewCommand = new AsyncRelayCommand(openDifficultyPreviewAsync);

        // The header doubles as the title bar. Windows and Linux get our own caption buttons;
        // macOS keeps the system traffic lights, so the header leaves room for them.
        ShowWindowButtons = !OperatingSystem.IsMacOS();
        TitleBarPadding = OperatingSystem.IsMacOS() ? new Thickness(84, 8, 16, 8) : new Thickness(16, 8, 8, 8);
        InitializeComponent();

        if (OperatingSystem.IsMacOS())
        {
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome | ExtendClientAreaChromeHints.OSXThickTitleBar;
        }

        TitleBar.PointerPressed += onTitleBarPressed;
        TitleBar.DoubleTapped += onTitleBarDoubleTapped;
        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximizeButton.Click += (_, _) => toggleMaximized();
        CloseButton.Click += (_, _) => Close();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == WindowStateProperty)
            {
                updateMaximizeGlyph();
            }
        };
        updateMaximizeGlyph();
    }

    public MainWindow(MainWindowViewModel viewModel, IVideoPlayer videoPlayer, IAudioEngine audioEngine)
        : this()
    {
        ArgumentNullException.ThrowIfNull(videoPlayer);
        ArgumentNullException.ThrowIfNull(audioEngine);
        this.viewModel = viewModel;
        this.audioEngine = audioEngine;
        DataContext = viewModel;
        videoSurface = videoPlayer.Surface as MediaPlayer;

        // The storyboard follows the audio engine's clock directly so animation stays
        // smooth; the view model's 100 ms position updates are too coarse for that.
        StoryboardView.Clock = () => audioEngine.CurrentTime;
        StudioShell.StoryboardSurface.Clock = () => audioEngine.CurrentTime;

        // LibVLCSharp.Avalonia only hands the native window handle to libVLC inside the
        // MediaPlayer setter, and the handle does not exist until the view is attached to
        // the visual tree. Assigning here (before the window is shown) would leave libVLC
        // without a handle and it would open its own window, so assign on every attach.
        VideoView.AttachedToVisualTree += (_, _) => attachVideoSurface();
        StudioShell.VideoSurface.AttachedToVisualTree += (_, _) => attachVideoSurface();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainWindowViewModel.IsStudioInterface) or nameof(MainWindowViewModel.IsTheaterMode))
                Avalonia.Threading.Dispatcher.UIThread.Post(attachVideoSurface);
        };

        // Double-clicking a row plays it; the first click of the pair already selected it.
        TrackList.DoubleTapped += (_, args) =>
        {
            if (viewModel.SelectedTrack is not null && args.Source is Visual source
                && source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null
                && source.FindAncestorOfType<Button>(includeSelf: true) is null)
            {
                viewModel.PlaySelectedCommand.Execute(null);
            }
        };

        // Shortcuts are handled while tunnelling so they win over focused buttons (Space) and lists.
        AddHandler(KeyDownEvent, onKeyDown, RoutingStrategies.Tunnel);

        Opened += async (_, _) =>
        {
            if (!initialized)
            {
                initialized = true;
                await viewModel.InitializeAsync();
            }
        };

        // Stop the sound before the window disappears; the rest of the shutdown runs afterwards.
        Closing += (_, _) =>
        {
            previewWindow?.Close();
            visualsWindow?.Close();
            viewModel.PrepareForShutdown();
        };
    }

    public IRelayCommand ToggleVisualsWindowCommand { get; }

    public IRelayCommand FullscreenVisualsCommand { get; }

    public IAsyncRelayCommand OpenDifficultyPreviewCommand { get; }

    /// <summary>Whether the header shows minimize / maximize / close (false on macOS, which keeps its own).</summary>
    public bool ShowWindowButtons { get; }

    public Thickness TitleBarPadding { get; }

    private void onTitleBarPressed(object? sender, PointerPressedEventArgs args)
    {
        // Buttons and the search box handle their own presses, so only empty header space gets here.
        if (args.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !isInteractive(args.Source))
        {
            BeginMoveDrag(args);
        }
    }

    private void onTitleBarDoubleTapped(object? sender, TappedEventArgs args)
    {
        if (ShowWindowButtons && !isInteractive(args.Source))
        {
            toggleMaximized();
        }
    }

    private static bool isInteractive(object? source) =>
        source is Visual visual && (visual.FindAncestorOfType<Button>(includeSelf: true) is not null || visual.FindAncestorOfType<TextBox>(includeSelf: true) is not null);

    private void toggleMaximized() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void updateMaximizeGlyph()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeIcon.Data = this.FindResource(maximized ? "IconRestore" : "IconMaximize") as Geometry;
        ToolTip.SetTip(MaximizeButton, maximized ? "Restore" : "Maximize");
    }

    private void attachVideoSurface()
    {
        if (videoSurface is null || visualsWindow is not null)
        {
            return;
        }

        // Re-assigning forces the view to detach and attach again with the current handle.
        VideoView.MediaPlayer = null;
        StudioShell.VideoSurface.MediaPlayer = null;
        var surface = viewModel?.IsStudioInterface == true ? StudioShell.VideoSurface : VideoView;
        surface.MediaPlayer = videoSurface;
        viewModel?.RestartVideoSurface(); // no-op unless a video is loaded (i.e. when coming back from the pop-out)
    }

    private void onKeyDown(object? sender, KeyEventArgs args)
    {
        if (viewModel is null)
        {
            return;
        }

        var focused = FocusManager?.GetFocusedElement();
        var typing = focused is TextBox || (focused as Visual)?.FindAncestorOfType<TextBox>() is not null;
        if (ShortcutMap.Resolve(args.Key, args.KeyModifiers, typing) is not { } action)
        {
            return;
        }

        var handled = action switch
        {
            ShortcutAction.FocusSearch => focusSearch(),
            ShortcutAction.PopOutVisuals => toggleVisualsWindow(fullscreen: false),
            ShortcutAction.ToggleFullscreenVisuals => toggleVisualsWindow(fullscreen: true),
            ShortcutAction.OpenDifficultyPreview => startDifficultyPreview(),
            _ => viewModel.TryHandleShortcut(action),
        };
        args.Handled = handled;
    }

    private bool focusSearch()
    {
        if (viewModel?.IsStudioInterface == true)
        {
            viewModel.CloseStudioToolsCommand.Execute(null);
            StudioShell.FocusSearch();
            return true;
        }
        SearchBox.Focus();
        SearchBox.SelectAll();
        return true;
    }

    private bool startDifficultyPreview()
    {
        _ = openDifficultyPreviewAsync();
        return true;
    }

    private bool toggleVisualsWindow(bool fullscreen)
    {
        if (visualsWindow is { } open)
        {
            if (fullscreen && open.WindowState != WindowState.FullScreen)
            {
                open.WindowState = WindowState.FullScreen;
                open.Activate();
            }
            else
            {
                open.Close();
            }

            return true;
        }

        openVisualsWindow(fullscreen);
        return true;
    }

    private void openVisualsWindow(bool fullscreen)
    {
        if (viewModel is null || audioEngine is null)
        {
            return;
        }

        if (visualsWindow is null)
        {
            // Hand the single libVLC surface to the popup; it comes back when the popup closes.
            VideoView.MediaPlayer = null;
            StudioShell.VideoSurface.MediaPlayer = null;
            var window = new VisualsWindow(viewModel, videoSurface, audioEngine);
            window.Closed += (_, _) =>
            {
                visualsWindow = null;
                viewModel.IsVisualsPoppedOut = false;
                attachVideoSurface();
            };
            visualsWindow = window;
            viewModel.IsVisualsPoppedOut = true;
            window.Show(this);
        }

        if (fullscreen)
        {
            visualsWindow.WindowState = WindowState.FullScreen;
        }

        visualsWindow.Activate();
    }

    private async Task openDifficultyPreviewAsync()
    {
        if (viewModel is null || audioEngine is null)
        {
            return;
        }

        if (!await viewModel.OpenDifficultyPreviewAsync())
        {
            return;
        }

        if (previewWindow is null)
        {
            var window = new DifficultyPreviewWindow(viewModel, audioEngine);
            window.Closed += (_, _) =>
            {
                previewWindow = null;
                viewModel.CloseDifficultyPreview();
            };
            previewWindow = window;
            window.Show(this);
        }

        previewWindow.Activate();
    }
}
