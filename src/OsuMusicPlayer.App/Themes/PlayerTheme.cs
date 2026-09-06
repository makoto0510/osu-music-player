using System.Globalization;
using Avalonia.Media;

namespace OsuMusicPlayer.App.Themes;

/// <summary>
/// The colours the player paints with. Presets cover the usual tastes; the accent can be
/// overridden with any hex colour so users can match their osu! skin.
/// </summary>
public sealed record PlayerTheme(
    string Name,
    bool IsDark,
    Color Background,
    Color Surface,
    Color SurfaceAlt,
    Color Border,
    Color Text,
    Color TextMuted,
    Color TextFaint,
    Color Accent)
{
    /// <summary>Text drawn on top of the accent colour.</summary>
    public Color AccentText => relativeLuminance(Accent) > 0.45 ? Color.FromRgb(0x14, 0x14, 0x1A) : Colors.White;

    public PlayerTheme WithAccent(Color accent) => this with { Accent = accent };

    private static double relativeLuminance(Color color)
    {
        static double channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * channel(color.R) + 0.7152 * channel(color.G) + 0.0722 * channel(color.B);
    }
}

public static class PlayerThemes
{
    public const string DefaultName = "osu! Pink";

    public static IReadOnlyList<PlayerTheme> Presets { get; } =
    [
        dark(DefaultName, "#FF66AA"),
        dark("Lazer Purple", "#A06BFF", background: "#120F1C", surface: "#1A1526", surfaceAlt: "#2A2340", border: "#332B4D"),
        dark("Midnight Blue", "#4FA3FF", background: "#0D1220", surface: "#121A2C", surfaceAlt: "#1E2A44", border: "#27355A"),
        dark("Forest", "#5CD68A", background: "#0F1512", surface: "#141C18", surfaceAlt: "#20302A", border: "#2A3D34"),
        dark("OLED Black", "#FF66AA", background: "#000000", surface: "#0A0A0A", surfaceAlt: "#1C1C1C", border: "#262626"),
        light("Daylight", "#E0447F"),
    ];

    public static IReadOnlyList<string> Names { get; } = Presets.Select(static theme => theme.Name).ToArray();

    /// <summary>Finds a preset by name (falling back to the default) and applies an optional accent override.</summary>
    public static PlayerTheme Resolve(string? name, string? accentHex)
    {
        var theme = Presets.FirstOrDefault(theme => string.Equals(theme.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Presets[0];
        return TryParseColor(accentHex, out var accent) ? theme.WithAccent(accent) : theme;
    }

    /// <summary>Accepts "#RRGGBB", "RRGGBB" and "#AARRGGBB"; anything else (including blank) is rejected.</summary>
    public static bool TryParseColor(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var hex = text.Trim();
        if (hex.StartsWith('#'))
        {
            hex = hex[1..];
        }

        if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        if (hex.Length == 6)
        {
            value |= 0xFF000000;
        }

        color = Color.FromUInt32(value);
        return true;
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>Lightens (positive) or darkens (negative) a colour by a fraction of the distance to white / black.</summary>
    public static Color Shade(Color color, double amount)
    {
        var target = amount >= 0 ? 255 : 0;
        var t = Math.Clamp(Math.Abs(amount), 0, 1);
        byte mix(byte channel) => (byte)Math.Round(channel + (target - channel) * t);
        return Color.FromArgb(color.A, mix(color.R), mix(color.G), mix(color.B));
    }

    private static PlayerTheme dark(string name, string accent, string background = "#0F0D14", string surface = "#16131D", string surfaceAlt = "#252030", string border = "#2B2536") =>
        new(name, true, parse(background), parse(surface), parse(surfaceAlt), parse(border), parse("#F2F3F7"), parse("#AEB4C4"), parse("#737A8D"), parse(accent));

    private static PlayerTheme light(string name, string accent) =>
        new(name, false, parse("#F4F5F9"), parse("#FFFFFF"), parse("#E6E8F0"), parse("#D6D9E3"), parse("#1B1D26"), parse("#4E5468"), parse("#8A90A3"), parse(accent));

    private static Color parse(string hex) => TryParseColor(hex, out var color) ? color : throw new ArgumentException($"Bad colour {hex}", nameof(hex));
}
