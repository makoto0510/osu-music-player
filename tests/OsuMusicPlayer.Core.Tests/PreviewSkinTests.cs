using FluentAssertions;
using OsuMusicPlayer.Core.Preview;
using OsuMusicPlayer.Core.Skins;

namespace OsuMusicPlayer.Core.Tests;

public sealed class PreviewSkinTests
{
    [Fact]
    public void Parse_ReadsGeneralColoursFontsAndManiaSections()
    {
        var skin = PreviewSkinLoader.Parse("""
            [General]
            Name: My Skin // trailing comment
            Author:Someone
            AllowSliderBallTint: 1
            CursorCentre: 0

            [Colours]
            Combo3 : 10,20,30
            Combo1 : 255,0,0
            Combo2: 0, 255, 0, 128
            SliderBorder: 1,2,3
            SliderTrackOverride: 4,5,6
            SliderBall: 7,8,9
            Combo4: not a colour

            [Fonts]
            HitCirclePrefix: fonts\numbers
            HitCircleOverlap: 5

            [Mania]
            Keys: 4
            Colour1: 1,1,1
            Colour2: 2,2,2
            ColourHold: 9,9,9

            [Mania]
            Keys: 7
            Colour1: 3,3,3
            """.Split('\n'), null, "Folder");

        skin.Name.Should().Be("My Skin");
        skin.Author.Should().Be("Someone");
        skin.AllowSliderBallTint.Should().BeTrue();
        skin.CursorCentre.Should().BeFalse();
        skin.ComboColours.Should().Equal(new PreviewComboColour(255, 0, 0), new PreviewComboColour(0, 255, 0), new PreviewComboColour(10, 20, 30));
        skin.SliderBorder.Should().Be(new PreviewComboColour(1, 2, 3));
        skin.SliderTrackOverride.Should().Be(new PreviewComboColour(4, 5, 6));
        skin.SliderBall.Should().Be(new PreviewComboColour(7, 8, 9));
        skin.HitCirclePrefix.Should().Be("fonts/numbers");
        skin.HitCircleOverlap.Should().Be(5);
        skin.Mania.Should().HaveCount(2);
        skin.ManiaFor(4)!.Columns.Should().Equal(new PreviewComboColour(1, 1, 1), new PreviewComboColour(2, 2, 2));
        skin.ManiaFor(4)!.Hold.Should().Be(new PreviewComboColour(9, 9, 9));
        skin.ManiaFor(7)!.Hold.Should().BeNull();
        skin.ManiaFor(5).Should().BeNull();
        skin.IsDefault.Should().BeTrue("no directory was given");
    }

    [Fact]
    public void Parse_FallsBackToTheFolderNameAndDefaults()
    {
        var skin = PreviewSkinLoader.Parse(["[Colours]", "Combo1: 1,2,3"], null, "Folder");

        skin.Name.Should().Be("Folder");
        skin.HitCirclePrefix.Should().Be("default");
        skin.HitCircleOverlap.Should().Be(-2);
        skin.AllowSliderBallTint.Should().BeFalse();
        skin.CursorCentre.Should().BeTrue();
        skin.SliderBorder.Should().BeNull();
    }

    [Theory]
    [InlineData("255,128,0", true, 255, 128, 0)]
    [InlineData(" 1 , 2 , 3 , 255 ", true, 1, 2, 3)]
    [InlineData("300,-5,12.6", true, 255, 0, 13)]
    [InlineData("1,2", false, 0, 0, 0)]
    [InlineData("a,b,c", false, 0, 0, 0)]
    [InlineData("", false, 0, 0, 0)]
    public void TryParseColour_AcceptsOsuTriplets(string text, bool expected, int r, int g, int b)
    {
        PreviewSkinLoader.TryParseColour(text, out var colour).Should().Be(expected);
        if (expected)
        {
            colour.Should().Be(new PreviewComboColour((byte)r, (byte)g, (byte)b));
        }
    }

    [Fact]
    public void Load_ReadsSkinIniAndFindsImagesPreferringHighResolution()
    {
        using var directory = new TestDirectory();
        var folder = directory.CreateDirectory("Cool Skin");
        directory.CreateFile(Path.Combine("Cool Skin", "skin.ini"), "[General]\nName: Cool\n");
        directory.CreateFile(Path.Combine("Cool Skin", "hitcircle.png"));
        directory.CreateFile(Path.Combine("Cool Skin", "hitcircle@2x.png"));
        directory.CreateFile(Path.Combine("Cool Skin", "cursor.jpg"));
        directory.CreateFile(Path.Combine("Cool Skin", "sliderb0.png"));
        directory.CreateFile(Path.Combine("Cool Skin", "sliderb1.png"));
        directory.CreateFile(Path.Combine("Cool Skin", "sliderb2@2x.png"));
        directory.CreateFile(Path.Combine("Cool Skin", "spinner-circle.png"));
        directory.CreateFile(Path.Combine("Cool Skin", "followpoint-0.png"));
        directory.CreateFile(Path.Combine("Cool Skin", "followpoint-1.png"));

        var skin = PreviewSkinLoader.Load(folder);

        skin.Name.Should().Be("Cool");
        skin.Directory.Should().Be(folder);
        skin.IsDefault.Should().BeFalse();
        skin.FindImage("hitcircle").Should().Be(new SkinImage(Path.Combine(folder, "hitcircle@2x.png"), 2f));
        skin.FindImage("cursor").Should().Be(new SkinImage(Path.Combine(folder, "cursor.jpg"), 1f));
        skin.FindImage("approachcircle").Should().BeNull();
        skin.FindAnimation("sliderb").Select(static frame => Path.GetFileName(frame.Path)).Should().Equal("sliderb0.png", "sliderb1.png", "sliderb2@2x.png");
        skin.FindAnimation("followpoint").Should().HaveCount(2, "osu! also numbers frames with a hyphen");
        skin.FindAnimation("spinner-circle").Should().ContainSingle("a still image is a one-frame animation");
        skin.FindAnimation("nothing").Should().BeEmpty();
    }

    [Fact]
    public void Load_WithoutSkinIni_UsesTheFolderName_AndMissingFolderThrows()
    {
        using var directory = new TestDirectory();
        var folder = directory.CreateDirectory("Bare");

        PreviewSkinLoader.Load(folder).Name.Should().Be("Bare");
        PreviewSkinLoader.Load(folder, "Shown").Name.Should().Be("Shown");
        var load = () => PreviewSkinLoader.Load(Path.Combine(directory.Path, "missing"));
        load.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void Default_HasNoImages()
    {
        PreviewSkin.Default.IsDefault.Should().BeTrue();
        PreviewSkin.Default.FindImage("hitcircle").Should().BeNull();
        PreviewSkin.Default.FindAnimation("sliderb").Should().BeEmpty();
    }

    [Fact]
    public void Catalog_ListsOwnFoldersThenStableSkins_AndCreatesTheRootWithAReadMe()
    {
        using var directory = new TestDirectory();
        var root = Path.Combine(directory.Path, "Documents", "OsuMusicPlayer", "Skins");
        var catalog = new PreviewSkinCatalog(root);

        catalog.Enumerate().Should().BeEmpty("a missing root is not an error");
        catalog.EnsureRootExists().Should().BeTrue();
        File.ReadAllText(Path.Combine(root, PreviewSkinCatalog.ReadMeFileName)).Should().Contain("skin.ini");

        directory.CreateDirectory("Documents", "OsuMusicPlayer", "Skins", "zeta");
        directory.CreateDirectory("Documents", "OsuMusicPlayer", "Skins", "Alpha");
        directory.CreateDirectory("Documents", "OsuMusicPlayer", "Skins", ".hidden");
        directory.CreateFile(Path.Combine("Documents", "OsuMusicPlayer", "Skins", "loose-file.png"));
        var stable = directory.CreateDirectory("osu!");
        directory.CreateDirectory("osu!", "Skins", "Rafis");

        var entries = catalog.Enumerate([stable, Path.Combine(directory.Path, "no such install")]);

        entries.Select(static entry => entry.Name).Should().Equal("Alpha", "zeta", "osu!stable: Rafis");
        entries[2].Directory.Should().Be(Path.Combine(stable, "Skins", "Rafis"));
        PreviewSkinCatalog.Find(entries, "ALPHA")!.Directory.Should().Be(Path.Combine(root, "Alpha"));
        PreviewSkinCatalog.Find(entries, "Default").Should().BeNull();
        PreviewSkinCatalog.Find(entries, "").Should().BeNull();
        PreviewSkinCatalog.Find(entries, "unknown").Should().BeNull();
    }

    [Fact]
    public void Catalog_DefaultRootIsUnderTheUsersDocuments()
    {
        var root = PreviewSkinCatalog.GetDefaultRootPath();

        root.Should().EndWith(Path.Combine("OsuMusicPlayer", "Skins"));
        Path.IsPathRooted(root).Should().BeTrue();
    }
}
