using OsuMusicPlayer.App.Controls;
using OsuMusicPlayer.Core.Skins;
using SkiaSharp;

namespace OsuMusicPlayer.App.Tests;

public sealed class SkinSpritesTests
{
    [Fact]
    public void Get_DecodesImagesOnceReportsSizesInOnexPixelsAndCachesMisses()
    {
        using var directory = new TestDirectory();
        var folder = directory.CreateDirectory("skin");
        writePng(Path.Combine(folder, "hitcircle@2x.png"), 256, 256);
        writePng(Path.Combine(folder, "cursor.png"), 40, 20);
        directory.CreateFile(Path.Combine("skin", "approachcircle.png"), "not a png");
        writePng(Path.Combine(folder, "sliderb0.png"), 8, 8);
        writePng(Path.Combine(folder, "sliderb1.png"), 8, 8);

        var sprites = new SkinSprites(PreviewSkinLoader.Load(folder));

        var hitCircle = sprites.Get("hitcircle");
        hitCircle.Should().NotBeNull();
        hitCircle!.Scale.Should().Be(2f);
        hitCircle.Width.Should().Be(128f, "sizes are normalised to 1x skin pixels");
        hitCircle.Image.Width.Should().Be(256);
        sprites.Get("hitcircle").Should().BeSameAs(hitCircle, "decoded images are cached");

        var cursor = sprites.Get("cursor")!;
        (cursor.Width, cursor.Height).Should().Be((40f, 20f));
        sprites.Get("approachcircle").Should().BeNull("a corrupt file falls back to vectors");
        sprites.Get("missing").Should().BeNull();
        sprites.Frames("sliderb").Should().HaveCount(2);
        sprites.Frames("sliderb").Should().BeSameAs(sprites.Frames("sliderb"));
    }

    [Fact]
    public void Default_NeverTouchesDisk()
    {
        SkinSprites.Default.Skin.IsDefault.Should().BeTrue();
        SkinSprites.Default.Get("hitcircle").Should().BeNull();
        SkinSprites.Default.Frames("sliderb").Should().BeEmpty();
    }

    private static void writePng(string path, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.White);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }
}
