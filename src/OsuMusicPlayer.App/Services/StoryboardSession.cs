using OsuMusicPlayer.Core.Models;
using ReOsuStoryboardPlayer.Core.Base;
using ReOsuStoryboardPlayer.Core.Commands.Group.Trigger;
using ReOsuStoryboardPlayer.Core.Kernel;
using SkiaSharp;

namespace OsuMusicPlayer.App.Services;

/// <summary>One drawable sprite, captured from the storyboard state for a single frame.</summary>
public readonly record struct StoryboardSpriteFrame(
    SKImage Image,
    float X,
    float Y,
    float ScaleX,
    float ScaleY,
    float Rotation,
    byte Red,
    byte Green,
    byte Blue,
    byte Alpha,
    bool Additive,
    bool FlipHorizontal,
    bool FlipVertical,
    float OriginX,
    float OriginY);

/// <summary>
/// A parsed storyboard together with its decoded textures. Update it with the audio
/// clock, then read <see cref="CaptureFrame"/> to get what to draw. Textures are owned by
/// the session; they are released once the session is disposed and no render operation
/// still holds a lease on them.
/// </summary>
public sealed class StoryboardSession : IDisposable
{
    private readonly StoryboardUpdater updater;
    private readonly List<StoryboardObject> triggerObjects;
    private readonly Dictionary<string, SKImage?> textures;
    private readonly List<StoryboardSpriteFrame> frame = [];
    private readonly object sync = new();
    private int renderLeases;
    private bool disposed;
    private bool texturesReleased;

    internal StoryboardSession(List<StoryboardObject> objects, Dictionary<string, SKImage?> textures, bool widescreen)
    {
        ArgumentNullException.ThrowIfNull(objects);
        this.textures = textures ?? throw new ArgumentNullException(nameof(textures));
        ObjectCount = objects.Count;
        TextureCount = textures.Values.Count(static image => image is not null);
        Widescreen = widescreen;
        triggerObjects = objects.Where(static item => item.ContainTrigger).ToList();
        updater = new StoryboardUpdater(objects);
    }

    /// <summary>
    /// Fires the storyboard's HitSound triggers for a hit that just played. The parser
    /// registers every trigger sprite with the shared listener, so this session's sprites
    /// react and stale ones are removed on dispose.
    /// </summary>
    public void Trigger(HitsoundEvent hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        if (triggerObjects.Count == 0)
        {
            return;
        }

        var soundType = HitObjectSoundType.None;
        var sampleSet = SampleSetType.None;
        var additions = SampleSetType.None;
        var customSet = CustomSampleSetType.Default;
        foreach (var sample in hit.Samples)
        {
            var set = sample.SampleSetName switch
            {
                "soft" => SampleSetType.Soft,
                "drum" => SampleSetType.Drum,
                _ => SampleSetType.Normal,
            };
            switch (sample.SoundName)
            {
                case "hitnormal":
                case "custom":
                    soundType |= HitObjectSoundType.Normal;
                    sampleSet = set;
                    break;
                case "hitwhistle":
                    soundType |= HitObjectSoundType.Whistle;
                    additions = set;
                    break;
                case "hitfinish":
                    soundType |= HitObjectSoundType.Finish;
                    additions = set;
                    break;
                case "hitclap":
                    soundType |= HitObjectSoundType.Clap;
                    additions = set;
                    break;
                default:
                    continue; // slider ticks do not trigger storyboards
            }

            customSet = sample.CustomIndex switch
            {
                1 => CustomSampleSetType.Custom1,
                2 => CustomSampleSetType.Custom2,
                _ => CustomSampleSetType.Default,
            };
        }

        if (soundType == HitObjectSoundType.None)
        {
            return;
        }

        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            var time = (float)hit.Time.TotalMilliseconds;
            TriggerListener.DefaultListener.Trig(new HitSoundInfo(hit.Time.TotalMilliseconds, soundType, sampleSet, customSet, additions == SampleSetType.None ? sampleSet : additions), time);
        }
    }

    public int ObjectCount { get; }

    public int TextureCount { get; }

    /// <summary>True when the map declares a 16:9 storyboard (x from -107 to 747).</summary>
    public bool Widescreen { get; }

    /// <summary>Advances the storyboard to <paramref name="time"/> and snapshots the visible sprites.</summary>
    public void Update(TimeSpan time)
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            updater.Update((float)time.TotalMilliseconds);
            frame.Clear();
            foreach (var storyboardObject in updater.UpdatingStoryboardObjects.OrderBy(static item => item.layer).ThenBy(static item => item.Z))
            {
                if (!storyboardObject.IsVisible || storyboardObject is StoryboardBackgroundObject || storyboardObject.ImageFilePath is null)
                {
                    continue;
                }

                if (!textures.TryGetValue(storyboardObject.ImageFilePath, out var image) || image is null)
                {
                    continue;
                }

                var color = storyboardObject.Color;
                frame.Add(new StoryboardSpriteFrame(
                    image,
                    storyboardObject.Postion.X,
                    storyboardObject.Postion.Y,
                    storyboardObject.Scale.X,
                    storyboardObject.Scale.Y,
                    storyboardObject.Rotate,
                    color.X,
                    color.Y,
                    color.Z,
                    color.W,
                    storyboardObject.IsAdditive,
                    storyboardObject.IsHorizonFlip,
                    storyboardObject.IsVerticalFlip,
                    (float)storyboardObject.OriginOffset.X + 0.5f,
                    0.5f - (float)storyboardObject.OriginOffset.Y));
            }
        }
    }

    /// <summary>
    /// Copies the sprites of the last update for a render operation and takes a lease on the
    /// textures. Returns an empty frame once disposed. Every non-empty capture must be paired
    /// with <see cref="ReleaseRenderLease"/>.
    /// </summary>
    public StoryboardSpriteFrame[] CaptureFrame()
    {
        lock (sync)
        {
            if (disposed)
            {
                return [];
            }

            renderLeases++;
            return frame.ToArray();
        }
    }

    /// <summary>Called by a render operation when it no longer draws the captured frame.</summary>
    public void ReleaseRenderLease()
    {
        lock (sync)
        {
            renderLeases = Math.Max(0, renderLeases - 1);
            if (disposed && renderLeases == 0)
            {
                releaseTextures();
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            frame.Clear();
            foreach (var storyboardObject in triggerObjects)
            {
                TriggerListener.DefaultListener.Remove(storyboardObject);
            }

            if (renderLeases == 0)
            {
                releaseTextures();
            }
        }
    }

    private void releaseTextures()
    {
        if (texturesReleased)
        {
            return;
        }

        texturesReleased = true;
        foreach (var image in textures.Values)
        {
            image?.Dispose();
        }

        textures.Clear();
    }
}
