using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using OsuMusicPlayer.App.Services;
using SkiaSharp;

namespace OsuMusicPlayer.App.Controls;

/// <summary>
/// Draws a <see cref="StoryboardSession"/> with Skia so additive blending and rotation
/// behave like osu!. The control advances the storyboard from <see cref="Clock"/> once per
/// animation frame while it is attached and has a session.
/// </summary>
public sealed class StoryboardView : Control
{
    public static readonly StyledProperty<StoryboardSession?> SessionProperty =
        AvaloniaProperty.Register<StoryboardView, StoryboardSession?>(nameof(Session));

    private const float storyboard_width = 640f;
    private const float storyboard_height = 480f;

    private bool frameRequested;

    static StoryboardView()
    {
        AffectsRender<StoryboardView>(SessionProperty);
    }

    public StoryboardSession? Session
    {
        get => GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>Returns the current audio time; set by the window from the audio engine.</summary>
    public Func<TimeSpan>? Clock { get; set; }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        requestFrame();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SessionProperty || change.Property == IsVisibleProperty)
        {
            requestFrame();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        // Hiding this view (or an ancestor) stops the animation callback. Rendering
        // it again must restart the loop even when the session has not changed.
        requestFrame();
        var session = Session;
        if (session is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var sprites = session.CaptureFrame();
        if (sprites.Length == 0)
        {
            session.ReleaseRenderLease();
            return;
        }

        context.Custom(new StoryboardDrawOperation(new Rect(Bounds.Size), sprites, session));
    }

    private void requestFrame()
    {
        if (frameRequested || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        frameRequested = true;
        topLevel.RequestAnimationFrame(_ =>
        {
            frameRequested = false;
            if (Session is not { } session || Clock is not { } clock || !IsEffectivelyVisible || TopLevel.GetTopLevel(this) is null)
            {
                return;
            }

            session.Update(clock());
            InvalidateVisual();
            requestFrame();
        });
    }

    /// <summary>
    /// Holds a render lease on the session for as long as Avalonia may still draw this
    /// operation, so textures are never freed underneath the render thread.
    /// </summary>
    private sealed class StoryboardDrawOperation(Rect bounds, StoryboardSpriteFrame[] sprites, StoryboardSession session) : ICustomDrawOperation
    {
        private readonly bool widescreen = session.Widescreen;
        private int released;

        public Rect Bounds => bounds;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                session.ReleaseRenderLease();
            }
        }

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Render(ImmediateDrawingContext context)
        {
            var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (lease is null)
            {
                return;
            }

            using var api = lease.Lease();
            var canvas = api.SkCanvas;
            canvas.Save();
            canvas.ClipRect(new SKRect((float)bounds.X, (float)bounds.Y, (float)bounds.Right, (float)bounds.Bottom));

            // Fit the 640x480 playfield to the control height; widescreen maps use the extra
            // horizontal room (-107..747) and are letterboxed otherwise.
            var scale = (float)bounds.Height / storyboard_height;
            var visibleWidth = widescreen ? (float)bounds.Width / scale : storyboard_width;
            var offsetX = (float)bounds.X + ((float)bounds.Width - storyboard_width * scale) / 2f;
            if (!widescreen)
            {
                canvas.ClipRect(new SKRect(offsetX, (float)bounds.Y, offsetX + storyboard_width * scale, (float)bounds.Bottom));
            }

            canvas.Translate(offsetX, (float)bounds.Y);
            canvas.Scale(scale);

            using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.Medium };
            foreach (var sprite in sprites)
            {
                if (sprite.Alpha == 0 || sprite.ScaleX == 0 || sprite.ScaleY == 0)
                {
                    continue;
                }

                var width = sprite.Image.Width;
                var height = sprite.Image.Height;
                canvas.Save();
                canvas.Translate(sprite.X, sprite.Y);
                if (sprite.Rotation != 0)
                {
                    canvas.RotateRadians(sprite.Rotation);
                }

                canvas.Scale(sprite.ScaleX * (sprite.FlipHorizontal ? -1 : 1), sprite.ScaleY * (sprite.FlipVertical ? -1 : 1));
                paint.BlendMode = sprite.Additive ? SKBlendMode.Plus : SKBlendMode.SrcOver;
                paint.ColorFilter = sprite is { Red: 255, Green: 255, Blue: 255, Alpha: 255 }
                    ? null
                    : SKColorFilter.CreateBlendMode(new SKColor(sprite.Red, sprite.Green, sprite.Blue, sprite.Alpha), SKBlendMode.Modulate);
                canvas.DrawImage(sprite.Image, -sprite.OriginX * width, -sprite.OriginY * height, paint);
                canvas.Restore();
            }

            _ = visibleWidth;
            canvas.Restore();
        }
    }
}
