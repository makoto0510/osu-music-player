using Avalonia.Input;

namespace OsuMusicPlayer.App.Input;

public enum ShortcutAction
{
    TogglePlay,
    Next,
    Previous,
    PlaySelected,
    SeekForward,
    SeekBackward,
    VolumeUp,
    VolumeDown,
    ToggleFavourite,
    EnqueueSelected,
    ToggleShuffle,
    CycleRepeat,
    FocusSearch,
    ClearSearch,
    ToggleQueue,
    ToggleSettings,
    ToggleTheater,
    PopOutVisuals,
    ToggleFullscreenVisuals,
    OpenDifficultyPreview,
}

/// <summary>One keyboard gesture. <see cref="WorksWhileTyping"/> gestures also fire inside text boxes.</summary>
public sealed record Shortcut(ShortcutAction Action, Key Key, KeyModifiers Modifiers, string Description, bool WorksWhileTyping = true)
{
    public string GestureText
    {
        get
        {
            var parts = new List<string>(4);
            if (Modifiers.HasFlag(KeyModifiers.Control))
            {
                parts.Add("Ctrl");
            }

            if (Modifiers.HasFlag(KeyModifiers.Shift))
            {
                parts.Add("Shift");
            }

            if (Modifiers.HasFlag(KeyModifiers.Alt))
            {
                parts.Add("Alt");
            }

            parts.Add(Key switch
            {
                Key.Space => "Space",
                Key.Escape => "Esc",
                Key.Return => "Enter",
                Key.Left => "←",
                Key.Right => "→",
                Key.Up => "↑",
                Key.Down => "↓",
                Key.OemComma => ",",
                Key.OemQuestion => "/",
                Key.MediaPlayPause => "Media Play/Pause",
                Key.MediaNextTrack => "Media Next",
                Key.MediaPreviousTrack => "Media Previous",
                _ => Key.ToString(),
            });
            return string.Join("+", parts);
        }
    }
}

/// <summary>
/// The player's keyboard shortcuts. Plain keys (Space, arrows, letters) are ignored while a
/// text box has focus so typing a search still works; chords and media keys always apply.
/// </summary>
public static class ShortcutMap
{
    public static IReadOnlyList<Shortcut> All { get; } =
    [
        new(ShortcutAction.TogglePlay, Key.Space, KeyModifiers.None, "Play / pause", WorksWhileTyping: false),
        new(ShortcutAction.TogglePlay, Key.MediaPlayPause, KeyModifiers.None, "Play / pause"),
        new(ShortcutAction.PlaySelected, Key.Return, KeyModifiers.None, "Play the selected track", WorksWhileTyping: false),
        new(ShortcutAction.Next, Key.Right, KeyModifiers.Control, "Next track"),
        new(ShortcutAction.Next, Key.MediaNextTrack, KeyModifiers.None, "Next track"),
        new(ShortcutAction.Previous, Key.Left, KeyModifiers.Control, "Previous track"),
        new(ShortcutAction.Previous, Key.MediaPreviousTrack, KeyModifiers.None, "Previous track"),
        new(ShortcutAction.SeekForward, Key.Right, KeyModifiers.Shift, "Seek +5 s"),
        new(ShortcutAction.SeekBackward, Key.Left, KeyModifiers.Shift, "Seek −5 s"),
        new(ShortcutAction.VolumeUp, Key.Up, KeyModifiers.Control, "Volume +5 %"),
        new(ShortcutAction.VolumeDown, Key.Down, KeyModifiers.Control, "Volume −5 %"),
        new(ShortcutAction.ToggleFavourite, Key.D, KeyModifiers.Control, "Favourite / unfavourite"),
        new(ShortcutAction.EnqueueSelected, Key.E, KeyModifiers.Control, "Add the selected track to the queue"),
        new(ShortcutAction.ToggleShuffle, Key.S, KeyModifiers.Control, "Shuffle on / off"),
        new(ShortcutAction.CycleRepeat, Key.R, KeyModifiers.Control, "Repeat off / all / one"),
        new(ShortcutAction.FocusSearch, Key.F, KeyModifiers.Control, "Search"),
        new(ShortcutAction.FocusSearch, Key.OemQuestion, KeyModifiers.None, "Search", WorksWhileTyping: false),
        new(ShortcutAction.ClearSearch, Key.Escape, KeyModifiers.None, "Clear the search / leave fullscreen"),
        new(ShortcutAction.ToggleQueue, Key.Q, KeyModifiers.Control, "Show / hide the now-playing pane"),
        new(ShortcutAction.ToggleSettings, Key.OemComma, KeyModifiers.Control, "Show / hide settings"),
        new(ShortcutAction.ToggleTheater, Key.T, KeyModifiers.Control, "Theater mode"),
        new(ShortcutAction.PopOutVisuals, Key.P, KeyModifiers.Control, "Video / storyboard in its own window"),
        new(ShortcutAction.ToggleFullscreenVisuals, Key.F11, KeyModifiers.None, "Video / storyboard fullscreen"),
        new(ShortcutAction.OpenDifficultyPreview, Key.P, KeyModifiers.Control | KeyModifiers.Shift, "Preview the selected difficulty"),
    ];

    /// <summary>Returns the action for a key press, or <see langword="null"/> when none (or when typing blocks it).</summary>
    public static ShortcutAction? Resolve(Key key, KeyModifiers modifiers, bool isTyping)
    {
        var effective = modifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta);
        foreach (var shortcut in All)
        {
            if (shortcut.Key == key && shortcut.Modifiers == effective && (!isTyping || shortcut.WorksWhileTyping))
            {
                return shortcut.Action;
            }
        }

        return null;
    }
}
