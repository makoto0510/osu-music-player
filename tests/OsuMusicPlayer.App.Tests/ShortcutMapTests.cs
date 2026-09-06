using Avalonia.Input;
using OsuMusicPlayer.App.Input;

namespace OsuMusicPlayer.App.Tests;

public sealed class ShortcutMapTests
{
    [Fact]
    public void PlainKeys_AreIgnoredWhileTyping_ButChordsAlwaysApply()
    {
        ShortcutMap.Resolve(Key.Space, KeyModifiers.None, isTyping: false).Should().Be(ShortcutAction.TogglePlay);
        ShortcutMap.Resolve(Key.Space, KeyModifiers.None, isTyping: true).Should().BeNull("a space in the search box is a space");
        ShortcutMap.Resolve(Key.Return, KeyModifiers.None, isTyping: true).Should().BeNull();
        ShortcutMap.Resolve(Key.F, KeyModifiers.Control, isTyping: true).Should().Be(ShortcutAction.FocusSearch);
        ShortcutMap.Resolve(Key.Escape, KeyModifiers.None, isTyping: true).Should().Be(ShortcutAction.ClearSearch);
        ShortcutMap.Resolve(Key.MediaNextTrack, KeyModifiers.None, isTyping: true).Should().Be(ShortcutAction.Next);
    }

    [Fact]
    public void Modifiers_MustMatchExactly()
    {
        ShortcutMap.Resolve(Key.Right, KeyModifiers.Control, isTyping: false).Should().Be(ShortcutAction.Next);
        ShortcutMap.Resolve(Key.Right, KeyModifiers.Shift, isTyping: false).Should().Be(ShortcutAction.SeekForward);
        ShortcutMap.Resolve(Key.Right, KeyModifiers.None, isTyping: false).Should().BeNull("plain arrows move the list selection");
        ShortcutMap.Resolve(Key.P, KeyModifiers.Control, isTyping: false).Should().Be(ShortcutAction.PopOutVisuals);
        ShortcutMap.Resolve(Key.P, KeyModifiers.Control | KeyModifiers.Shift, isTyping: false).Should().Be(ShortcutAction.OpenDifficultyPreview);
        ShortcutMap.Resolve(Key.Z, KeyModifiers.Control, isTyping: false).Should().BeNull();
    }

    [Fact]
    public void EveryShortcut_HasADescriptionAndAReadableGesture()
    {
        ShortcutMap.All.Should().NotBeEmpty();
        ShortcutMap.All.Should().AllSatisfy(shortcut =>
        {
            shortcut.Description.Should().NotBeNullOrWhiteSpace();
            shortcut.GestureText.Should().NotBeNullOrWhiteSpace();
        });
        ShortcutMap.All.Single(static shortcut => shortcut.Action == ShortcutAction.ToggleSettings).GestureText.Should().Be("Ctrl+,");
        ShortcutMap.All.First(static shortcut => shortcut.Action == ShortcutAction.SeekBackward).GestureText.Should().Be("Shift+←");
        ShortcutMap.All.Select(static shortcut => (shortcut.Key, shortcut.Modifiers)).Should().OnlyHaveUniqueItems("two actions on one gesture would be ambiguous");
    }
}
