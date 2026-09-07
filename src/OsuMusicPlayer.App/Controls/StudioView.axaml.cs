using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using LibVLCSharp.Avalonia;
using OsuMusicPlayer.App.ViewModels;

namespace OsuMusicPlayer.App.Controls;

public sealed partial class StudioView : UserControl
{
    public StudioView()
    {
        InitializeComponent();
        StudioTitleBar.PointerPressed += (_, args) =>
        {
            if (TopLevel.GetTopLevel(this) is Window window
                && args.GetCurrentPoint(this).Properties.IsLeftButtonPressed
                && args.Source is Avalonia.Visual source
                && source.FindAncestorOfType<Button>(includeSelf: true) is null)
                window.BeginMoveDrag(args);
        };
        StudioTitleBar.DoubleTapped += (_, args) =>
        {
            if (args.Source is Avalonia.Visual source
                && source.FindAncestorOfType<Button>(includeSelf: true) is null)
                toggleMaximized();
        };
        StudioMinimize.Click += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window window) window.WindowState = WindowState.Minimized;
        };
        StudioMaximize.Click += (_, _) => toggleMaximized();
        StudioClose.Click += (_, _) => (TopLevel.GetTopLevel(this) as Window)?.Close();
        StudioTrackList.DoubleTapped += (_, args) =>
        {
            if (DataContext is MainWindowViewModel model && args.Source is Avalonia.Visual source
                && source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null
                && source.FindAncestorOfType<Button>(includeSelf: true) is null)
                model.PlaySelectedCommand.Execute(null);
        };
    }

    internal VideoView VideoSurface => StudioVideo;
    internal StoryboardView StoryboardSurface => StudioStoryboard;

    internal void FocusSearch()
    {
        if (DataContext is MainWindowViewModel model) model.IsTheaterMode = false;
        StudioSearchBox.Focus();
        StudioSearchBox.SelectAll();
    }

    private void toggleMaximized()
    {
        if (TopLevel.GetTopLevel(this) is Window window)
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
}
