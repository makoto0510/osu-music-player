using System.Globalization;
using Avalonia.Media;
using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.App.ViewModels;

public sealed class DifficultyItemViewModel(UnifiedBeatmap model)
{
    // osu!'s classic difficulty colours, keyed by the upper bound of each band.
    private static readonly (double MaxStars, IBrush Brush)[] star_bands =
    [
        (2.0, new SolidColorBrush(Color.Parse("#88B300"))),
        (2.7, new SolidColorBrush(Color.Parse("#66CCFF"))),
        (4.0, new SolidColorBrush(Color.Parse("#FFCC22"))),
        (5.3, new SolidColorBrush(Color.Parse("#FF66AA"))),
        (6.5, new SolidColorBrush(Color.Parse("#AA66FF"))),
        (double.PositiveInfinity, new SolidColorBrush(Color.Parse("#9A9A9A"))),
    ];

    private static readonly IBrush unknown_brush = new SolidColorBrush(Color.Parse("#4A4F5C"));

    public UnifiedBeatmap Model { get; } = model ?? throw new ArgumentNullException(nameof(model));

    public string Name => string.IsNullOrWhiteSpace(Model.DifficultyName) ? "(Unnamed)" : Model.DifficultyName;

    public string RulesetText => Model.Ruleset switch
    {
        OsuRuleset.Taiko => "taiko",
        OsuRuleset.Catch => "catch",
        OsuRuleset.Mania => "mania",
        OsuRuleset.Unknown => "other",
        _ => "osu!",
    };

    public double StarRating => Model.StarRating;

    public string StarText => Model.StarRating > 0
        ? string.Create(CultureInfo.InvariantCulture, $"{Model.StarRating:0.00}★")
        : "—★";

    public IBrush StarColor => Model.StarRating > 0
        ? star_bands.First(band => Model.StarRating < band.MaxStars).Brush
        : unknown_brush;

    public string StatsText => Model.Ruleset switch
    {
        OsuRuleset.Taiko => format(("OD", Model.OverallDifficulty), ("HP", Model.DrainRate)),
        OsuRuleset.Mania => format(("Keys", Model.CircleSize), ("OD", Model.OverallDifficulty), ("HP", Model.DrainRate)),
        _ => format(("CS", Model.CircleSize), ("AR", Model.ApproachRate), ("OD", Model.OverallDifficulty), ("HP", Model.DrainRate)),
    };

    public string BpmText => string.Create(CultureInfo.InvariantCulture, $"{Model.BPM:0.#} BPM");

    public string LengthText => Model.Length.ToString(Model.Length.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

    private static string format(params (string Label, double Value)[] values) =>
        string.Join("  ", values.Select(static pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Label} {pair.Value:0.#}")));
}
