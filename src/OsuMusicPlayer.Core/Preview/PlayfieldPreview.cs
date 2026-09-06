using System.Numerics;
using OsuMusicPlayer.Core.Models;
using OsuParsers.Beatmaps;
using OsuParsers.Beatmaps.Objects;
using OsuParsers.Decoders;
using OsuParsers.Enums;
using OsuParsers.Enums.Beatmaps;

namespace OsuMusicPlayer.Core.Preview;

public enum PreviewObjectKind
{
    Circle,
    Slider,
    Spinner,
    ManiaNote,
    ManiaHold,
    TaikoDon,
    TaikoKat,
    TaikoDrumroll,
    TaikoSwell,
    CatchFruit,
    CatchJuiceStream,
    CatchBananaShower,
}

/// <summary>
/// One drawable hit object. Positions are in osu! playfield pixels (512 × 384); the path is
/// the full slider body (one span) and <see cref="Repeats"/> is the number of spans.
/// </summary>
public sealed record PreviewObject(
    PreviewObjectKind Kind,
    TimeSpan StartTime,
    TimeSpan EndTime,
    Vector2 Position,
    IReadOnlyList<Vector2> Path,
    int Repeats,
    int Column,
    int ComboNumber,
    int ComboColourIndex,
    bool IsLarge)
{
    public TimeSpan Duration => EndTime - StartTime;
}

public sealed record PreviewComboColour(byte R, byte G, byte B);

/// <summary>Everything the preview renderer needs; immutable so it can be shared with the render thread.</summary>
public sealed record PlayfieldPreviewData(
    OsuRuleset Ruleset,
    IReadOnlyList<PreviewObject> Objects,
    double CircleRadius,
    TimeSpan Preempt,
    TimeSpan FadeIn,
    int ColumnCount,
    IReadOnlyList<PreviewComboColour> ComboColours)
{
    /// <summary>
    /// True when <see cref="ComboColours"/> came from the beatmap's [Colours] section rather than
    /// the built-in defaults, so a skin's own colours only replace them when the user asks.
    /// </summary>
    public bool HasBeatmapComboColours { get; init; }

    public TimeSpan FirstObjectTime => Objects.Count == 0 ? TimeSpan.Zero : Objects[0].StartTime;

    public TimeSpan LastObjectTime => Objects.Count == 0 ? TimeSpan.Zero : Objects.Max(static hitObject => hitObject.EndTime);
}

/// <summary>Reads a .osu file into <see cref="PlayfieldPreviewData"/> for the difficulty preview popup.</summary>
public static class PlayfieldPreviewBuilder
{
    private static readonly PreviewComboColour[] default_colours =
    [
        new(255, 192, 0),
        new(0, 202, 0),
        new(18, 124, 255),
        new(242, 24, 57),
    ];

    public static PlayfieldPreviewData Build(string beatmapPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(beatmapPath);
        using var stream = new FileStream(beatmapPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        lock (BeatmapDecoderGate.Sync)
        {
            return Build(BeatmapDecoder.Decode(stream));
        }
    }

    public static PlayfieldPreviewData Build(IEnumerable<string> beatmapLines)
    {
        ArgumentNullException.ThrowIfNull(beatmapLines);
        lock (BeatmapDecoderGate.Sync)
        {
            return Build(BeatmapDecoder.Decode(beatmapLines));
        }
    }

    internal static PlayfieldPreviewData Build(Beatmap beatmap)
    {
        ArgumentNullException.ThrowIfNull(beatmap);
        var ruleset = beatmap.GeneralSection.Mode switch
        {
            Ruleset.Taiko => OsuRuleset.Taiko,
            Ruleset.Fruits => OsuRuleset.Catch,
            Ruleset.Mania => OsuRuleset.Mania,
            _ => OsuRuleset.Osu,
        };

        var circleSize = beatmap.DifficultySection.CircleSize;
        var approachRate = beatmap.DifficultySection.ApproachRate;
        var columns = ruleset == OsuRuleset.Mania ? Math.Clamp((int)Math.Round(circleSize), 1, 18) : 0;
        var hasBeatmapColours = beatmap.ColoursSection.ComboColours is { Count: > 0 };
        var colours = hasBeatmapColours
            ? beatmap.ColoursSection.ComboColours.Select(static colour => new PreviewComboColour(colour.R, colour.G, colour.B)).ToArray()
            : default_colours;

        var objects = new List<PreviewObject>(beatmap.HitObjects.Count);
        var comboNumber = 0;
        var comboColour = -1;
        foreach (var hitObject in beatmap.HitObjects.OrderBy(static hitObject => hitObject.StartTime))
        {
            var isSpinner = hitObject is Spinner;
            if (ruleset is OsuRuleset.Osu or OsuRuleset.Catch)
            {
                if (hitObject.IsNewCombo || comboColour < 0 || isSpinner)
                {
                    comboNumber = 0;
                    comboColour = comboColour < 0 ? 0 : comboColour + 1 + Math.Max(0, hitObject.ComboOffset); // renderers wrap this around their palette
                }

                if (!isSpinner)
                {
                    comboNumber++;
                }
            }

            objects.Add(convert(hitObject, ruleset, columns, comboNumber, Math.Max(0, comboColour)));
        }

        return new PlayfieldPreviewData(
            ruleset,
            objects,
            CircleRadius: 54.4 - 4.48 * circleSize,
            Preempt: TimeSpan.FromMilliseconds(difficultyRange(approachRate, 1800, 1200, 450)),
            FadeIn: TimeSpan.FromMilliseconds(difficultyRange(approachRate, 1200, 800, 300)),
            columns,
            colours)
        {
            HasBeatmapComboColours = hasBeatmapColours,
        };
    }

    /// <summary>osu!'s piecewise mapping of a 0..10 difficulty value onto min / mid / max.</summary>
    public static double difficultyRange(double difficulty, double min, double mid, double max) => difficulty switch
    {
        > 5 => mid + (max - mid) * (difficulty - 5) / 5,
        < 5 => mid - (mid - min) * (5 - difficulty) / 5,
        _ => mid,
    };

    private static PreviewObject convert(HitObject hitObject, OsuRuleset ruleset, int columns, int comboNumber, int comboColour)
    {
        var start = TimeSpan.FromMilliseconds(hitObject.StartTime);
        var end = TimeSpan.FromMilliseconds(Math.Max(hitObject.EndTime, hitObject.StartTime));
        var position = hitObject.Position;
        IReadOnlyList<Vector2> path = [];
        var repeats = 1;
        var kind = PreviewObjectKind.Circle;

        if (hitObject is Slider slider)
        {
            var controlPoints = new List<Vector2>(slider.SliderPoints.Count + 1);
            if (slider.SliderPoints.Count == 0 || slider.SliderPoints[0] != position)
            {
                controlPoints.Add(position);
            }

            controlPoints.AddRange(slider.SliderPoints);
            path = SliderPathCalculator.Calculate(toKind(slider.CurveType), controlPoints, slider.PixelLength);
            repeats = Math.Max(1, slider.Repeats);
            kind = PreviewObjectKind.Slider;
        }
        else if (hitObject is Spinner)
        {
            kind = PreviewObjectKind.Spinner;
        }
        else if (end > start)
        {
            kind = PreviewObjectKind.ManiaHold; // long objects that are neither sliders nor spinners (mania holds)
        }

        var isLarge = hitObject.HitSound.HasFlag(HitSoundType.Finish);
        switch (ruleset)
        {
            case OsuRuleset.Mania:
                var column = Math.Clamp((int)Math.Floor(position.X * columns / 512f), 0, Math.Max(0, columns - 1));
                kind = end > start ? PreviewObjectKind.ManiaHold : PreviewObjectKind.ManiaNote;
                return new PreviewObject(kind, start, end, position, path, 1, column, 0, 0, false);
            case OsuRuleset.Taiko:
                kind = kind switch
                {
                    PreviewObjectKind.Slider => PreviewObjectKind.TaikoDrumroll,
                    PreviewObjectKind.Spinner => PreviewObjectKind.TaikoSwell,
                    _ => hitObject.HitSound.HasFlag(HitSoundType.Whistle) || hitObject.HitSound.HasFlag(HitSoundType.Clap) ? PreviewObjectKind.TaikoKat : PreviewObjectKind.TaikoDon,
                };
                return new PreviewObject(kind, start, end, position, path, repeats, 0, 0, 0, isLarge);
            case OsuRuleset.Catch:
                kind = kind switch
                {
                    PreviewObjectKind.Slider => PreviewObjectKind.CatchJuiceStream,
                    PreviewObjectKind.Spinner => PreviewObjectKind.CatchBananaShower,
                    _ => PreviewObjectKind.CatchFruit,
                };
                return new PreviewObject(kind, start, end, position, path, repeats, 0, comboNumber, comboColour, false);
            default:
                return new PreviewObject(kind, start, end, position, path, repeats, 0, comboNumber, comboColour, isLarge);
        }
    }

    private static SliderCurveKind toKind(CurveType curveType) => curveType switch
    {
        CurveType.Linear => SliderCurveKind.Linear,
        CurveType.PerfectCurve => SliderCurveKind.PerfectCircle,
        CurveType.Catmull => SliderCurveKind.Catmull,
        _ => SliderCurveKind.Bezier,
    };
}
