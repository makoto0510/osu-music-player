using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using OsuMusicPlayer.App.Services;
using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.App.ViewModels;

public sealed class TrackItemViewModel : ObservableObject, IDisposable
{
    private const int thumbnail_height = 96;
    private const int detail_height = 420;

    private readonly IBackgroundImageLoader imageLoader;
    private readonly CancellationTokenSource imageCancellation = new();
    private Task<Bitmap?>? backgroundImage;
    private Task<Bitmap?>? largeBackgroundImage;
    private IReadOnlyList<DifficultyItemViewModel>? difficulties;
    private bool disposed;

    public TrackItemViewModel(UnifiedBeatmapSet model, IBackgroundImageLoader imageLoader)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        this.imageLoader = imageLoader ?? throw new ArgumentNullException(nameof(imageLoader));
    }

    public UnifiedBeatmapSet Model { get; }
    public string Title => firstNonEmpty(Model.TitleUnicode, Model.Title, "(Untitled)");
    public string Artist => firstNonEmpty(Model.ArtistUnicode, Model.Artist, "(Unknown artist)");
    public string Creator => Model.Creator;
    public string Tags => string.Join(' ', Model.Beatmaps.Select(static beatmap => beatmap.Tags));
    public double BPM => Model.Beatmaps.Count == 0 ? 0 : Model.Beatmaps.Max(static beatmap => beatmap.BPM);
    public TimeSpan Length => Model.Beatmaps.Count == 0 ? TimeSpan.Zero : Model.Beatmaps.Max(static beatmap => beatmap.Length);
    public double MaxStarRating => Model.Beatmaps.Count == 0 ? 0 : Model.Beatmaps.Max(static beatmap => beatmap.StarRating);
    public string BpmText => string.Create(CultureInfo.InvariantCulture, $"{BPM:0.#} BPM");
    public string LengthText => formatTime(Length);

    public string SourceText => Model.Source switch
    {
        BeatmapSource.Stable => "osu!stable",
        BeatmapSource.Lazer => "osu!lazer",
        _ => "osu!stable + osu!lazer",
    };

    public string StarText => MaxStarRating <= 0
        ? "no star rating"
        : Model.Beatmaps.Count == 1
            ? string.Create(CultureInfo.InvariantCulture, $"{MaxStarRating:0.00}★")
            : string.Create(CultureInfo.InvariantCulture, $"up to {MaxStarRating:0.00}★");

    public string DifficultyCountText => Model.Beatmaps.Count == 1 ? "1 difficulty" : $"{Model.Beatmaps.Count} difficulties";

    /// <summary>
    /// Where a preview starts: the first difficulty's preview point, or 40% into the
    /// track when the set defines none (the same rule osu! song select uses).
    /// </summary>
    public TimeSpan? PreviewTime
    {
        get
        {
            var defined = Model.Beatmaps.Select(static beatmap => beatmap.PreviewTime).FirstOrDefault(static time => time > TimeSpan.Zero);
            if (defined > TimeSpan.Zero)
            {
                return defined;
            }

            return Length > TimeSpan.Zero ? TimeSpan.FromTicks((long)(Length.Ticks * 0.4)) : null;
        }
    }

    public string PreviewTimeText => PreviewTime is { } preview ? formatTime(preview) : "start";

    public IReadOnlyList<DifficultyItemViewModel> Difficulties => difficulties ??= Model.Beatmaps
        .Select(static beatmap => new DifficultyItemViewModel(beatmap))
        .OrderBy(static item => item.Model.Ruleset)
        .ThenBy(static item => item.StarRating)
        .ThenBy(static item => item.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    public Task<Bitmap?> BackgroundImage =>
        backgroundImage ??= imageLoader.LoadAsync(Model.BackgroundFilePath, imageCancellation.Token, thumbnail_height);

    public Task<Bitmap?> LargeBackgroundImage =>
        largeBackgroundImage ??= imageLoader.LoadAsync(Model.BackgroundFilePath, imageCancellation.Token, detail_height);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        imageCancellation.Cancel();
        imageCancellation.Dispose();
        disposeImage(backgroundImage);
        disposeImage(largeBackgroundImage);
    }

    private static string firstNonEmpty(string first, string second, string fallback) =>
        !string.IsNullOrWhiteSpace(first) ? first : !string.IsNullOrWhiteSpace(second) ? second : fallback;

    private static string formatTime(TimeSpan time) =>
        time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

    private static void disposeImage(Task<Bitmap?>? image)
    {
        if (image is { IsCompletedSuccessfully: true })
        {
            image.Result?.Dispose();
        }
        else if (image is not null)
        {
            _ = image.ContinueWith(
                static task => task.Result?.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
        }
    }
}
