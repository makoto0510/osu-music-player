using OsuMusicPlayer.Core.Skins;
using SkiaSharp;

namespace OsuMusicPlayer.App.Controls;

/// <summary>
/// Decoded images of one <see cref="PreviewSkin"/>, loaded lazily and cached by element name.
/// Misses are cached too, so a skin that lacks an element costs one directory probe.
/// Instances are immutable per skin; swapping skins swaps the whole cache.
/// </summary>
public sealed class SkinSprites
{
    private readonly Dictionary<string, Sprite?> images = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<Sprite>> animations = new(StringComparer.OrdinalIgnoreCase);
    private readonly object sync = new();

    public SkinSprites(PreviewSkin skin)
    {
        Skin = skin ?? throw new ArgumentNullException(nameof(skin));
    }

    public static SkinSprites Default { get; } = new(PreviewSkin.Default);

    public PreviewSkin Skin { get; }

    /// <summary>The decoded element, or null when the skin has no (readable) file for it.</summary>
    public Sprite? Get(string name)
    {
        if (Skin.IsDefault)
        {
            return null;
        }

        lock (sync)
        {
            if (!images.TryGetValue(name, out var sprite))
            {
                sprite = decode(Skin.FindImage(name));
                images[name] = sprite;
            }

            return sprite;
        }
    }

    /// <summary>All frames of an animated element (see <see cref="PreviewSkin.FindAnimation"/>); empty when absent.</summary>
    public IReadOnlyList<Sprite> Frames(string name)
    {
        if (Skin.IsDefault)
        {
            return [];
        }

        lock (sync)
        {
            if (!animations.TryGetValue(name, out var frames))
            {
                frames = Skin.FindAnimation(name).Select(decode).Where(static frame => frame is not null).Select(static frame => frame!).ToArray();
                animations[name] = frames;
            }

            return frames;
        }
    }

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

        public float Width => Image.Width / Scale;

        public float Height => Image.Height / Scale;
    }
}
