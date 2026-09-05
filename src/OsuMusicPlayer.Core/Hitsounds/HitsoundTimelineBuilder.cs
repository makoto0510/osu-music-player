using OsuMusicPlayer.Core.Models;
using OsuParsers.Beatmaps;
using OsuParsers.Beatmaps.Objects;
using OsuParsers.Decoders;
using OsuParsers.Enums.Beatmaps;

namespace OsuMusicPlayer.Core.Hitsounds;

/// <summary>
/// Turns the hit objects of a .osu file into the list of samples osu! plays while the
/// map runs: circle hits, slider heads / repeats / ends and ticks, and spinner ends.
/// </summary>
public static class HitsoundTimelineBuilder
{
    private const double tick_end_tolerance_ms = 10;

    public static IReadOnlyList<HitsoundEvent> Build(string beatmapPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(beatmapPath);
        using var stream = new FileStream(beatmapPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Build(BeatmapDecoder.Decode(stream));
    }

    public static IReadOnlyList<HitsoundEvent> Build(IEnumerable<string> beatmapLines)
    {
        ArgumentNullException.ThrowIfNull(beatmapLines);
        return Build(BeatmapDecoder.Decode(beatmapLines));
    }

    internal static IReadOnlyList<HitsoundEvent> Build(Beatmap beatmap)
    {
        ArgumentNullException.ThrowIfNull(beatmap);

        var timingPoints = beatmap.TimingPoints.OrderBy(static point => point.Offset).ToArray();
        var defaultSet = beatmap.GeneralSection.SampleSet;
        var tickRate = beatmap.DifficultySection.SliderTickRate > 0 ? beatmap.DifficultySection.SliderTickRate : 1;
        var events = new List<HitsoundEvent>();

        foreach (var hitObject in beatmap.HitObjects)
        {
            if (hitObject is Slider slider)
            {
                addSlider(events, slider, timingPoints, defaultSet, beatmap, tickRate);
            }
            else if (hitObject is Spinner spinner)
            {
                addHit(events, spinner.EndTime, spinner.HitSound, SampleSet.None, SampleSet.None, spinner.Extras, timingAt(timingPoints, spinner.EndTime), defaultSet);
            }
            else
            {
                addHit(events, hitObject.StartTime, hitObject.HitSound, SampleSet.None, SampleSet.None, hitObject.Extras, timingAt(timingPoints, hitObject.StartTime), defaultSet);
            }
        }

        return events.OrderBy(static hit => hit.Time).ToArray();
    }

    private static void addSlider(List<HitsoundEvent> events, Slider slider, TimingPoint[] timingPoints, SampleSet defaultSet, Beatmap beatmap, double tickRate)
    {
        var spans = Math.Max(1, slider.Repeats);
        var spanDuration = Math.Max(0, slider.EndTime - slider.StartTime) / (double)spans;

        for (var edge = 0; edge <= spans; edge++)
        {
            var time = slider.StartTime + edge * spanDuration;
            var hitSound = slider.EdgeHitSounds is { Count: > 0 } edgeSounds && edge < edgeSounds.Count ? edgeSounds[edge] : slider.HitSound;
            var (edgeSet, edgeAddition) = slider.EdgeAdditions is { Count: > 0 } additions && edge < additions.Count
                ? (additions[edge].Item1, additions[edge].Item2)
                : (SampleSet.None, SampleSet.None);
            addHit(events, time, hitSound, edgeSet, edgeAddition, slider.Extras, timingAt(timingPoints, time), defaultSet);
        }

        var beatLength = beatmap.BeatLengthAt(slider.StartTime);
        if (!double.IsFinite(beatLength) || beatLength <= 0 || spanDuration <= 0)
        {
            return;
        }

        var tickInterval = beatLength / tickRate;
        if (tickInterval <= 0)
        {
            return;
        }

        for (var span = 0; span < spans; span++)
        {
            var spanStart = slider.StartTime + span * spanDuration;
            for (var offset = tickInterval; offset < spanDuration - tick_end_tolerance_ms; offset += tickInterval)
            {
                var time = spanStart + offset;
                var timing = timingAt(timingPoints, time);
                var set = resolveSet(SampleSet.None, slider.Extras.SampleSet, timing, defaultSet);
                events.Add(new HitsoundEvent(
                    TimeSpan.FromMilliseconds(time),
                    [new HitsoundSample(setName(set), "slidertick", resolveIndex(slider.Extras, timing), resolveVolume(slider.Extras, timing), null)]));
            }
        }
    }

    private static void addHit(
        List<HitsoundEvent> events,
        double timeMs,
        HitSoundType hitSound,
        SampleSet setOverride,
        SampleSet additionOverride,
        Extras extras,
        TimingPoint? timing,
        SampleSet defaultSet)
    {
        var set = resolveSet(setOverride, extras.SampleSet, timing, defaultSet);
        var addition = additionOverride != SampleSet.None ? additionOverride : extras.AdditionSet != SampleSet.None ? extras.AdditionSet : set;
        var index = resolveIndex(extras, timing);
        var volume = resolveVolume(extras, timing);
        var samples = new List<HitsoundSample>(4);

        if (!string.IsNullOrWhiteSpace(extras.SampleFileName))
        {
            // An explicit file replaces the whole hit sound; additions are not layered on top.
            samples.Add(new HitsoundSample(setName(set), "custom", index, volume, extras.SampleFileName.Trim()));
        }
        else
        {
            samples.Add(new HitsoundSample(setName(set), "hitnormal", index, volume, null));
            if (hitSound.HasFlag(HitSoundType.Whistle))
            {
                samples.Add(new HitsoundSample(setName(addition), "hitwhistle", index, volume, null));
            }

            if (hitSound.HasFlag(HitSoundType.Finish))
            {
                samples.Add(new HitsoundSample(setName(addition), "hitfinish", index, volume, null));
            }

            if (hitSound.HasFlag(HitSoundType.Clap))
            {
                samples.Add(new HitsoundSample(setName(addition), "hitclap", index, volume, null));
            }
        }

        events.Add(new HitsoundEvent(TimeSpan.FromMilliseconds(Math.Max(0, timeMs)), samples));
    }

    private static TimingPoint? timingAt(TimingPoint[] timingPoints, double timeMs)
    {
        TimingPoint? current = null;
        foreach (var point in timingPoints)
        {
            if (point.Offset <= timeMs + 1)
            {
                current = point;
            }
            else
            {
                break;
            }
        }

        return current ?? timingPoints.FirstOrDefault();
    }

    private static SampleSet resolveSet(SampleSet edgeSet, SampleSet extrasSet, TimingPoint? timing, SampleSet defaultSet)
    {
        var set = edgeSet != SampleSet.None ? edgeSet
            : extrasSet != SampleSet.None ? extrasSet
            : timing is { SampleSet: not SampleSet.None } ? timing.SampleSet
            : defaultSet;
        return set == SampleSet.None ? SampleSet.Normal : set;
    }

    private static int resolveIndex(Extras extras, TimingPoint? timing) =>
        extras.CustomIndex > 0 ? extras.CustomIndex : Math.Max(0, timing?.CustomSampleSet ?? 0);

    private static double resolveVolume(Extras extras, TimingPoint? timing)
    {
        var percent = extras.Volume > 0 ? extras.Volume : timing?.Volume ?? 100;
        return Math.Clamp(percent, 0, 100) / 100d;
    }

    private static string setName(SampleSet set) => set switch
    {
        SampleSet.Soft => "soft",
        SampleSet.Drum => "drum",
        _ => "normal",
    };
}
