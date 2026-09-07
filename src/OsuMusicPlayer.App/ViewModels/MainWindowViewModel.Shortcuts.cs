using OsuMusicPlayer.App.Input;

namespace OsuMusicPlayer.App.ViewModels;

/// <summary>Keyboard shortcut actions that do not map one-to-one onto an existing command.</summary>
public sealed partial class MainWindowViewModel
{
    private const double volume_step = 0.05;
    private static readonly TimeSpan seek_step = TimeSpan.FromSeconds(5);

    /// <summary>Runs a shortcut that the view model can handle on its own; returns false for view-level ones.</summary>
    public bool TryHandleShortcut(ShortcutAction action)
    {
        switch (action)
        {
            case ShortcutAction.TogglePlay:
                TogglePlayCommand.Execute(null);
                return true;
            case ShortcutAction.Next:
                _ = NextCommand.ExecuteAsync(null);
                return true;
            case ShortcutAction.Previous:
                _ = PreviousCommand.ExecuteAsync(null);
                return true;
            case ShortcutAction.PlaySelected:
                if (SelectedTrack is null)
                {
                    return false;
                }

                _ = PlaySelectedCommand.ExecuteAsync(null);
                return true;
            case ShortcutAction.SeekForward:
                SeekBy(seek_step);
                return true;
            case ShortcutAction.SeekBackward:
                SeekBy(-seek_step);
                return true;
            case ShortcutAction.VolumeUp:
                Volume = Math.Clamp(Math.Round(Volume + volume_step, 2), 0, 1);
                return true;
            case ShortcutAction.VolumeDown:
                Volume = Math.Clamp(Math.Round(Volume - volume_step, 2), 0, 1);
                return true;
            case ShortcutAction.ToggleFavourite:
                ToggleFavouriteCommand.Execute(null);
                return true;
            case ShortcutAction.EnqueueSelected:
                EnqueueSelectedCommand.Execute(null);
                return true;
            case ShortcutAction.ToggleShuffle:
                IsShuffleEnabled = !IsShuffleEnabled;
                return true;
            case ShortcutAction.CycleRepeat:
                CycleRepeatCommand.Execute(null);
                return true;
            case ShortcutAction.ClearSearch:
                if (IsStudioInterface && IsStudioToolsVisible)
                {
                    CloseStudioTools();
                    return true;
                }
                if (IsStudioInterface && IsStudioQueueVisible)
                {
                    IsStudioQueueVisible = false;
                    return true;
                }
                if (string.IsNullOrEmpty(SearchText))
                {
                    return false;
                }

                SearchText = string.Empty;
                return true;
            case ShortcutAction.ToggleQueue:
                if (IsStudioInterface) IsStudioQueueVisible = !IsStudioQueueVisible;
                else IsNowPlayingPaneVisible = !IsNowPlayingPaneVisible;
                return true;
            case ShortcutAction.ToggleSettings:
                IsSettingsPanelVisible = !IsSettingsPanelVisible;
                return true;
            case ShortcutAction.ToggleTheater:
                IsTheaterMode = !IsTheaterMode;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Moves the playhead by a relative amount, clamped to the track.</summary>
    public void SeekBy(TimeSpan delta)
    {
        if (CurrentTrack is null || TotalTime <= TimeSpan.Zero)
        {
            return;
        }

        var target = CurrentTime + delta;
        if (target < TimeSpan.Zero)
        {
            target = TimeSpan.Zero;
        }
        else if (target > TotalTime)
        {
            target = TotalTime;
        }

        seek(target.TotalSeconds / TotalTime.TotalSeconds);
    }
}
