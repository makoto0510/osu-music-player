using Avalonia.Controls;
using Avalonia.Media;
using OsuMusicPlayer.App.Themes;

namespace OsuMusicPlayer.App.Tests;

public sealed class PlayerThemeTests
{
    [Fact]
    public void Resolve_FallsBackToTheDefaultPresetAndAppliesAValidAccent()
    {
        PlayerThemes.Resolve(null, null).Name.Should().Be(PlayerThemes.DefaultName);
        PlayerThemes.Resolve("no such theme", null).Name.Should().Be(PlayerThemes.DefaultName);
        PlayerThemes.Resolve("midnight blue", null).Name.Should().Be("Midnight Blue", "names are case-insensitive");

        var custom = PlayerThemes.Resolve("Daylight", "#123456");
        custom.IsDark.Should().BeFalse();
        custom.Accent.Should().Be(Color.FromRgb(0x12, 0x34, 0x56));
        PlayerThemes.Resolve("Daylight", "not a colour").Accent.Should().Be(PlayerThemes.Resolve("Daylight", null).Accent, "an invalid accent keeps the preset's");
    }

    [Theory]
    [InlineData("#FF66AA", true, 0xFF, 0x66, 0xAA)]
    [InlineData("ff66aa", true, 0xFF, 0x66, 0xAA)]
    [InlineData(" #80FF66AA ", true, 0xFF, 0x66, 0xAA)]
    [InlineData("#FFF", false, 0, 0, 0)]
    [InlineData("", false, 0, 0, 0)]
    [InlineData("#GGGGGG", false, 0, 0, 0)]
    public void TryParseColor_AcceptsSixAndEightDigitHex(string text, bool expected, int r, int g, int b)
    {
        PlayerThemes.TryParseColor(text, out var colour).Should().Be(expected);
        if (expected)
        {
            (colour.R, colour.G, colour.B).Should().Be(((byte)r, (byte)g, (byte)b));
        }
    }

    [Fact]
    public void AccentText_ContrastsWithTheAccent()
    {
        PlayerThemes.Resolve(null, "#FFFFFF").AccentText.Should().NotBe(Colors.White, "white text on a white accent is unreadable");
        PlayerThemes.Resolve(null, "#101010").AccentText.Should().Be(Colors.White);
    }

    [Fact]
    public void Shade_MovesTowardsWhiteOrBlack()
    {
        var mid = Color.FromRgb(100, 100, 100);
        PlayerThemes.Shade(mid, 0.5).R.Should().BeGreaterThan(100);
        PlayerThemes.Shade(mid, -0.5).R.Should().BeLessThan(100);
        PlayerThemes.Shade(mid, 1).Should().Be(Colors.White);
        PlayerThemes.ToHex(mid).Should().Be("#646464");
    }

    [Fact]
    public void Apply_WritesBrushesAndTheFluentAccentPalette_AndUpdatesInPlace()
    {
        var resources = new ResourceDictionary();
        ApplicationThemeApplier.Apply(resources, PlayerThemes.Resolve("Forest", null));

        resources["ThemeAccentBrush"].Should().BeOfType<SolidColorBrush>().Which.Color.Should().Be(Color.FromRgb(0x5C, 0xD6, 0x8A));
        resources["SystemAccentColor"].Should().Be(Color.FromRgb(0x5C, 0xD6, 0x8A));
        resources.ContainsKey("SystemAccentColorLight1").Should().BeTrue();
        resources.ContainsKey("ThemeSelectionBrush").Should().BeTrue();
        resources["ThemeButtonBrush"].Should().BeOfType<SolidColorBrush>().Which.Color.Should().Be(Colors.White);
        resources["ThemeButtonTextBrush"].Should().BeOfType<SolidColorBrush>().Which.Color.Should().Be(Color.FromRgb(0x14, 0x14, 0x1A));

        var brush = (SolidColorBrush)resources["ThemeAccentBrush"]!;
        ApplicationThemeApplier.Apply(resources, PlayerThemes.Resolve("Forest", "#112233"));
        brush.Color.Should().Be(Color.FromRgb(0x11, 0x22, 0x33), "the existing brush is recoloured so bound controls update");
    }
}
