using System.Collections.Concurrent;
using OsuMusicPlayer.Core.Skins;
using SkiaSharp;

namespace OsuMusicPlayer.App.Controls;

/// <summary>
/// Decoded images of one <see cref="PreviewSkin"/>, loaded lazily and cached by element name.
/// Misses are cached too, so a skin that lacks an element costs one dictionary lookup.
/// Instances are immutable per skin; swapping skins swaps the whole cache.
/// </summary>
public sealed class SkinSprites
{
    private readonly ConcurrentDictionary<string, Sprite?> images = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<Sprite>> animations = new(StringComparer.OrdinalIgnoreCase);
    private readonly string[] digitNames;

    public SkinSprites(PreviewSkin skin)
    {
        Skin = skin ?? throw new ArgumentNullException(nameof(skin));
        digitNames = Enumerable.Range(0, 10).Select(digit => $"{skin.HitCirclePrefix}-{digit}").ToArray();
    }

    public static SkinSprites Default { get; } = new(PreviewSkin.Default);

    public PreviewSkin Skin { get; }

    /// <summary>The decoded element, or null when the skin has no (readable) file for it.</summary>
    public Sprite? Get(string name) => images.GetOrAdd(name, static (key, skin) => decode(skin.FindImage(key)), Skin);

    /// <summary>The combo-number glyph for one digit (0–9), following the skin's HitCirclePrefix.</summary>
    public Sprite? Digit(int digit) => Get(digitNames[digit]);

    /// <summary>All frames of an animated element (see <see cref="PreviewSkin.FindAnimation"/>); empty when absent.</summary>
    public IReadOnlyList<Sprite> Frames(string name) => animations.GetOrAdd(
        name,
        static (key, skin) => skin.FindAnimation(key).Select(decode).OfType<Sprite>().ToArray(),
        Skin);

    private static Sprite? decode(SkinImage? image)
    {
        if (image is null)
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(image.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var decoded = SKImage.FromEncodedData(stream);
            return decoded is null || decoded.Width == 0 || decoded.Height == 0 ? null : new Sprite(decoded, image.Scale);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>A decoded element; <see cref="Width"/> / <see cref="Height"/> are in 1x skin pixels regardless of the file's resolution.</summary>
    public sealed class Sprite(SKImage image, float scale)
    {
        public SKImage Image { get; } = image;

        public float Scale { get; } = scale;

        public float Width { get; } = image.Width / scale;

        public float Height { get; } = image.Height / scale;
    }
}
