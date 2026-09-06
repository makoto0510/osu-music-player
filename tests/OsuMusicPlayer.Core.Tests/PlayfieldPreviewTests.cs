using System.Numerics;
using FluentAssertions;
using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Core.Preview;

namespace OsuMusicPlayer.Core.Tests;

public sealed class PlayfieldPreviewTests
{
    private const string header = """
        osu file format v14

        [General]
        AudioFilename: audio.mp3
        Mode: {0}

        [Difficulty]
        HPDrainRate:5
        CircleSize:{1}
        OverallDifficulty:7
        ApproachRate:{2}
        SliderMultiplier:1.4
        SliderTickRate:1

        [TimingPoints]
        0,500,4,2,1,60,1,0

        """;

    [Fact]
    public void Standard_CirclesSlidersAndSpinners_GetComboNumbersColoursAndPaths()
    {
        var data = PlayfieldPreviewBuilder.Build(lines(0, 4, 9, """
            [Colours]
            Combo1 : 255,0,0
            Combo2 : 0,255,0

            [HitObjects]
            100,100,1000,5,0,0:0:0:0:
            200,100,1500,1,0,0:0:0:0:
            256,192,2000,6,0,L|356:192,2,100
            256,192,3000,12,0,4000
            300,300,4500,1,0,0:0:0:0:
            """));

        data.Ruleset.Should().Be(OsuRuleset.Osu);
        data.CircleRadius.Should().BeApproximately(54.4 - 4.48 * 4, 0.001);
        data.Preempt.Should().Be(TimeSpan.FromMilliseconds(600));
        data.FadeIn.Should().Be(TimeSpan.FromMilliseconds(400));
        data.ComboColours.Should().HaveCount(2);

        data.Objects.Should().HaveCount(5);
        data.Objects[0].Should().Match<PreviewObject>(static o => o.Kind == PreviewObjectKind.Circle && o.ComboNumber == 1 && o.ComboColourIndex == 0);
        data.Objects[1].Should().Match<PreviewObject>(static o => o.ComboNumber == 2 && o.ComboColourIndex == 0);

        var slider = data.Objects[2];
        slider.Kind.Should().Be(PreviewObjectKind.Slider);
        slider.ComboNumber.Should().Be(1, "the slider starts a new combo");
        slider.ComboColourIndex.Should().Be(1);
        slider.Repeats.Should().Be(2);
        slider.Path.First().Should().Be(new Vector2(256, 192));
        ((double)slider.Path.Last().X).Should().BeApproximately(356, 0.01);
        SliderPathCalculator.Length(slider.Path).Should().BeApproximately(100, 0.01);

        data.Objects[3].Kind.Should().Be(PreviewObjectKind.Spinner);
        data.Objects[3].EndTime.Should().Be(TimeSpan.FromMilliseconds(4000));
        data.Objects[4].ComboNumber.Should().Be(1, "a spinner resets the combo");
        data.LastObjectTime.Should().Be(TimeSpan.FromMilliseconds(4500));
    }

    [Fact]
    public void Mania_MapsColumnsFromXAndKeepsHolds()
    {
        var data = PlayfieldPreviewBuilder.Build(lines(3, 4, 5, """
            [HitObjects]
            64,192,1000,1,0,0:0:0:0:
            448,192,1000,128,0,2000:0:0:0:0:
            """));

        data.Ruleset.Should().Be(OsuRuleset.Mania);
        data.ColumnCount.Should().Be(4);
        data.Objects[0].Should().Match<PreviewObject>(static o => o.Kind == PreviewObjectKind.ManiaNote && o.Column == 0);
        data.Objects[1].Should().Match<PreviewObject>(static o => o.Kind == PreviewObjectKind.ManiaHold && o.Column == 3 && o.EndTime == TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Taiko_ClassifiesDonKatAndLarge()
    {
        var data = PlayfieldPreviewBuilder.Build(lines(1, 4, 5, """
            [HitObjects]
            100,100,1000,1,0,0:0:0:0:
            100,100,1200,1,8,0:0:0:0:
            100,100,1400,1,4,0:0:0:0:
            """));

        data.Objects.Select(static o => o.Kind).Should().Equal(PreviewObjectKind.TaikoDon, PreviewObjectKind.TaikoKat, PreviewObjectKind.TaikoDon);
        data.Objects[2].IsLarge.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 1800, 1200)]
    [InlineData(5, 1200, 800)]
    [InlineData(10, 450, 300)]
    public void ApproachRate_FollowsTheGameCurve(double approachRate, double preempt, double fadeIn)
    {
        PlayfieldPreviewBuilder.difficultyRange(approachRate, 1800, 1200, 450).Should().Be(preempt);
        PlayfieldPreviewBuilder.difficultyRange(approachRate, 1200, 800, 300).Should().Be(fadeIn);
    }

    private static IEnumerable<string> lines(int mode, double circleSize, double approachRate, string body) =>
        (string.Format(System.Globalization.CultureInfo.InvariantCulture, header, mode, circleSize, approachRate) + body)
            .Split('\n').Select(static line => line.TrimEnd('\r'));
}

public sealed class SliderPathCalculatorTests
{
    [Fact]
    public void Linear_TrimsToPixelLength()
    {
        var path = SliderPathCalculator.Calculate(SliderCurveKind.Linear, [new Vector2(0, 0), new Vector2(200, 0)], 150);

        path.Should().HaveCount(2);
        path[^1].Should().Be(new Vector2(150, 0));
        SliderPathCalculator.PositionAt(path, 0.5).Should().Be(new Vector2(75, 0));
    }

    [Fact]
    public void PerfectCircle_PassesThroughTheMiddlePointAndEndsAtTheLast()
    {
        var path = SliderPathCalculator.Calculate(SliderCurveKind.PerfectCircle, [new Vector2(0, 0), new Vector2(50, 50), new Vector2(100, 0)], 0);

        path.First().Should().Be(new Vector2(0, 0));
        ((double)path.Last().X).Should().BeApproximately(100, 0.01);
        ((double)path.Last().Y).Should().BeApproximately(0, 0.01);
        path.Should().Contain(point => Math.Abs(point.X - 50) < 4 && Math.Abs(point.Y - 50) < 4, "the arc passes through the middle control point");
        SliderPathCalculator.Length(path).Should().BeApproximately(Math.PI * 50, 0.5, "a semicircle of radius 50");
    }

    [Fact]
    public void Bezier_SplitsSegmentsAtRepeatedPoints()
    {
        var path = SliderPathCalculator.Calculate(SliderCurveKind.Bezier, [new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 0), new Vector2(100, 100)], 0);

        path.First().Should().Be(new Vector2(0, 0));
        path.Should().Contain(new Vector2(100, 0), "the repeated point is a hard corner");
        path.Last().Should().Be(new Vector2(100, 100));
        SliderPathCalculator.Length(path).Should().BeApproximately(200, 0.01);
    }

    [Fact]
    public void Catmull_StartsAndEndsOnControlPoints()
    {
        var path = SliderPathCalculator.Calculate(SliderCurveKind.Catmull, [new Vector2(0, 0), new Vector2(50, 40), new Vector2(100, 0)], 0);

        path.First().Should().Be(new Vector2(0, 0));
        path.Last().Should().Be(new Vector2(100, 0));
        path.Count.Should().BeGreaterThan(50);
    }
}
