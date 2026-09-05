using OsuMusicPlayer.Core.Models;

namespace OsuMusicPlayer.Audio;

/// <summary>
/// Walks a sorted hit sound timeline against a clock. Seeking backwards rewinds the
/// cursor; seeking forwards silently drops events that are older than the lateness limit.
/// </summary>
internal sealed class HitsoundScheduler
{
    private readonly IReadOnlyList<HitsoundEvent> events;
    private readonly TimeSpan maxLateness;
    private int index;
    private TimeSpan lastTime = TimeSpan.MinValue;

    public HitsoundScheduler(IReadOnlyList<HitsoundEvent> events, TimeSpan maxLateness)
    {
        this.events = events ?? throw new ArgumentNullException(nameof(events));
        this.maxLateness = maxLateness;
    }

    public TimeSpan? NextDue => index < events.Count ? events[index].Time : null;

    public IReadOnlyList<HitsoundEvent> Advance(TimeSpan now)
    {
        if (now < lastTime)
        {
            index = lowerBound(now);
        }

        lastTime = now;
        List<HitsoundEvent>? due = null;
        while (index < events.Count && events[index].Time <= now)
        {
            var hit = events[index++];
            if (now - hit.Time <= maxLateness)
            {
                (due ??= []).Add(hit);
            }
        }

        return due ?? (IReadOnlyList<HitsoundEvent>)[];
    }

    private int lowerBound(TimeSpan time)
    {
        var low = 0;
        var high = events.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (events[middle].Time < time)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
