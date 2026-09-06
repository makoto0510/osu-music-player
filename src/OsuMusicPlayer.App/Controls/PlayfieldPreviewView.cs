using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Core.Preview;
using OsuMusicPlayer.Core.Skins;
using SkiaSharp;

namespace OsuMusicPlayer.App.Controls;

/// <summary>
/// Draws a <see cref="PlayfieldPreviewData"/> as an auto-play, one frame per animation tick,
/// at the time returned by <see cref="Clock"/>. osu!, taiko, catch and mania each get a
/// simplified rendition of their playfield. Elements come from the <see cref="Skin"/> when it
/// provides them and are drawn as vectors otherwise.
/// </summary>
public sealed class PlayfieldPreviewView : Control
{
    public static readonly StyledProperty<PlayfieldPreviewData?> DataProperty =
        AvaloniaProperty.Register<PlayfieldPreviewView, PlayfieldPreviewData?>(nameof(Data));

    public static readonly StyledProperty<PreviewSkin?> SkinProperty =
        AvaloniaProperty.Register<PlayfieldPreviewView, PreviewSkin?>(nameof(Skin));

    /// <summary>When true the skin's combo colours win over the beatmap's (osu!'s "ignore beatmap skins").</summary>
    public static readonly StyledProperty<bool> PreferSkinColoursProperty =
        AvaloniaProperty.Register<PlayfieldPreviewView, bool>(nameof(PreferSkinColours));

    private SkinSprites sprites = SkinSprites.Default;
    private bool frameRequested;

    static PlayfieldPreviewView()
    {
        AffectsRender<PlayfieldPreviewView>(DataProperty, SkinProperty, PreferSkinColoursProperty);
    }

    public PlayfieldPreviewData? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public PreviewSkin? Skin
    {
        get => GetValue(SkinProperty);
        set => SetValue(SkinProperty, value);
    }

    public bool PreferSkinColours
    {
        get => GetValue(PreferSkinColoursProperty);
        set => SetValue(PreferSkinColoursProperty, value);
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
        if (change.Property == DataProperty)
        {
            requestFrame();
        }
        else if (change.Property == SkinProperty)
        {
            sprites = new SkinSprites(change.GetNewValue<PreviewSkin?>() ?? PreviewSkin.Default);
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Data is not { } data || Clock is not { } clock || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        context.Custom(new PreviewDrawOperation(new Rect(Bounds.Size), data, clock(), sprites, PreferSkinColours));
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
            if (Data is null || Clock is null || !IsEffectivelyVisible || TopLevel.GetTopLevel(this) is null)
            {
                return;
            }

            InvalidateVisual();
            requestFrame();
        });
    }

    private sealed class PreviewDrawOperation(Rect bounds, PlayfieldPreviewData data, TimeSpan time, SkinSprites sprites, bool preferSkinColours) : ICustomDrawOperation
    {
        private const float playfield_width = 512f;
        private const float playfield_height = 384f;

        /// <summary>osu! draws a 128 px (1x) hit circle at radius / 64, so this maps skin pixels to playfield pixels.</summary>
        private const float sprite_base_radius = 64f;
        private const float cursor_scale = 0.6f;
        private static readonly TimeSpan fade_out = TimeSpan.FromMilliseconds(240);
        private static readonly TimeSpan lookahead = TimeSpan.FromMilliseconds(1000);
        private static readonly TimeSpan slider_ball_frame = TimeSpan.FromMilliseconds(30);
        private static readonly SKColor taiko_don = new(235, 69, 43);
        private static readonly SKColor taiko_kat = new(67, 142, 173);
        private static readonly SKColor taiko_roll = new(255, 200, 40);
        private static readonly SKColor banana = new(255, 230, 60);

        /// <summary>Element names, built once so the per-frame loops never format strings.</summary>
        private static readonly SpritePair[] fruits = [new("fruit-pear"), new("fruit-grapes"), new("fruit-apple"), new("fruit-orange")];
        private static readonly SpritePair fruit_drop = new("fruit-drop");
        private static readonly SpritePair fruit_bananas = new("fruit-bananas");
        private static readonly SpritePair taiko_small = new("taikohitcircle", "taikohitcircleoverlay");
        private static readonly SpritePair taiko_large = new("taikobigcircle", "taikobigcircleoverlay");
        private static readonly ManiaNoteSprites[] mania_styles = [new("1"), new("2"), new("S")];

        /// <summary>Tint filters keyed by colour; a handful of combo colours exist, and the render thread is the only user.</summary>
        private static readonly Dictionary<SKColor, SKColorFilter> tint_filters = [];

        private readonly PreviewSkin skin = sprites.Skin;
        private SKPaint? spritePaint;

        public Rect Bounds => bounds;

        public void Dispose()
        {
            spritePaint?.Dispose();
            spritePaint = null;
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
            switch (data.Ruleset)
            {
                case OsuRuleset.Mania:
                    renderMania(canvas);
                    break;
                case OsuRuleset.Taiko:
                    renderTaiko(canvas);
                    break;
                case OsuRuleset.Catch:
                    renderCatch(canvas);
                    break;
                default:
                    renderStandard(canvas);
                    break;
            }

            canvas.Restore();
        }

        // ---------------------------------------------------------------- sprites

        /// <summary>
        /// Draws a skin element anchored at (x, y). <paramref name="scale"/> converts 1x skin pixels
        /// to canvas units; the tint multiplies the image (white leaves it untouched).
        /// </summary>
        private void drawSprite(SKCanvas canvas, SkinSprites.Sprite sprite, float x, float y, float scale, SKColor tint, float alpha, float rotationDegrees = 0, float anchorX = 0.5f, float anchorY = 0.5f)
        {
            var width = sprite.Width * scale;
            var height = sprite.Height * scale;
            canvas.Save();
            canvas.Translate(x, y);
            if (rotationDegrees != 0)
            {
                canvas.RotateDegrees(rotationDegrees);
            }

            drawImage(canvas, sprite, new SKRect(-width * anchorX, -height * anchorY, width * (1 - anchorX), height * (1 - anchorY)), tint, alpha);
            canvas.Restore();
        }

        /// <summary>Draws a skin element into an exact rectangle (also used to stretch hold bodies and drumrolls).</summary>
        private void drawImage(SKCanvas canvas, SkinSprites.Sprite sprite, SKRect destination, SKColor tint, float alpha)
        {
            if (alpha <= 0 || destination.Width <= 0 || destination.Height <= 0)
            {
                return;
            }

            var paint = spritePaint ??= new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.Medium };
            paint.Color = SKColors.White.WithAlpha((byte)(Math.Clamp(alpha, 0, 1) * 255));
            paint.ColorFilter = tintFilter(tint);
            canvas.DrawImage(sprite.Image, destination, paint);
        }

        private static SKColorFilter? tintFilter(SKColor tint)
        {
            if (tint == SKColors.White)
            {
                return null;
            }

            if (!tint_filters.TryGetValue(tint, out var filter))
            {
                filter = SKColorFilter.CreateBlendMode(tint, SKBlendMode.Modulate);
                tint_filters[tint] = filter;
            }

            return filter;
        }

        /// <summary>Draws an element plus its untinted overlay (hit circles, taiko circles, fruits); false when the skin lacks the element.</summary>
        private bool drawPair(SKCanvas canvas, SpritePair pair, float x, float y, float diameter, SKColor tint, float alpha)
        {
            if (sprites.Get(pair.Name) is not { } sprite)
            {
                return false;
            }

            var scale = fit(sprite, diameter);
            drawSprite(canvas, sprite, x, y, scale, tint, alpha);
            if (sprites.Get(pair.Overlay) is { } overlay)
            {
                drawSprite(canvas, overlay, x, y, scale, SKColors.White, alpha);
            }

            return true;
        }

        /// <summary>The scale that makes the sprite's larger side equal to <paramref name="size"/> canvas units.</summary>
        private static float fit(SkinSprites.Sprite sprite, float size) => size / Math.Max(1f, Math.Max(sprite.Width, sprite.Height));

        private static float fitWidth(SkinSprites.Sprite sprite, float width) => width / Math.Max(1f, sprite.Width);

        // ---------------------------------------------------------------- osu!standard

        private void renderStandard(SKCanvas canvas)
        {
            // Fit a 640x480 frame (playfield plus the game's margins) and centre the 512x384 field in it.
            var scale = (float)Math.Min(bounds.Width / 640, bounds.Height / 480);
            var originX = (float)bounds.X + ((float)bounds.Width - playfield_width * scale) / 2f;
            var originY = (float)bounds.Y + ((float)bounds.Height - playfield_height * scale) / 2f;
            canvas.Save();
            canvas.Translate(originX, originY);
            canvas.Scale(scale);

            using var border = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1, Color = new SKColor(255, 255, 255, 40) };
            canvas.DrawRect(0, 0, playfield_width, playfield_height, border);

            var radius = (float)data.CircleRadius;
            using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
            using var text = new SKPaint { IsAntialias = true, Color = SKColors.White, TextAlign = SKTextAlign.Center, TextSize = radius * 1.1f, FakeBoldText = true };

            // Later objects are drawn first so the next one to hit sits on top.
            foreach (var hitObject in visible(data.Preempt, fade_out).Reverse())
            {
                var colour = comboColour(hitObject.ComboColourIndex);
                var (alpha, approach) = approachState(hitObject);
                var position = hitObject.Position;
                switch (hitObject.Kind)
                {
                    case PreviewObjectKind.Spinner:
                        drawSpinner(canvas, hitObject, alpha, fill, stroke);
                        break;
                    case PreviewObjectKind.Slider:
                        drawSliderBody(canvas, hitObject, colour, alpha, radius, stroke);
                        if (!drawReverseArrow(canvas, hitObject, alpha, radius))
                        {
                            drawSliderEndMarker(canvas, hitObject, alpha, radius, stroke);
                        }

                        if (time >= hitObject.StartTime && time <= hitObject.EndTime)
                        {
                            drawSliderBall(canvas, hitObject, colour, radius, fill, stroke);
                        }

                        drawCircle(canvas, position, radius, colour, alpha, approach, hitObject.ComboNumber, fill, stroke, text);
                        break;
                    default:
                        drawCircle(canvas, position, radius, colour, alpha, approach, hitObject.ComboNumber, fill, stroke, text);
                        break;
                }
            }

            drawCursor(canvas, radius, fill, stroke);
            canvas.Restore();
        }

        private (float alpha, float approach) approachState(PreviewObject hitObject)
        {
            if (time < hitObject.StartTime)
            {
                var untilHit = hitObject.StartTime - time;
                var sinceAppear = data.Preempt - untilHit;
                var alpha = (float)Math.Clamp(sinceAppear.TotalMilliseconds / Math.Max(1, data.FadeIn.TotalMilliseconds), 0, 1);
                var approach = 1f + 3f * (float)Math.Clamp(untilHit.TotalMilliseconds / Math.Max(1, data.Preempt.TotalMilliseconds), 0, 1);
                return (alpha, approach);
            }

            var reference = hitObject.Kind == PreviewObjectKind.Slider ? hitObject.EndTime : hitObject.StartTime;
            if (time <= reference && hitObject.Kind == PreviewObjectKind.Slider)
            {
                return (1f, 0f);
            }

            var sinceHit = time - reference;
            return ((float)Math.Clamp(1 - sinceHit.TotalMilliseconds / fade_out.TotalMilliseconds, 0, 1), 0f);
        }

        private void drawCircle(SKCanvas canvas, Vector2 position, float radius, SKColor colour, float alpha, float approach, int number, SKPaint fill, SKPaint stroke, SKPaint text)
        {
            if (alpha <= 0)
            {
                return;
            }

            var byteAlpha = (byte)(alpha * 255);
            var pop = approach > 0 ? 1f : 1f + 0.35f * (1f - alpha); // hit circles grow while fading out
            var spriteScale = radius / sprite_base_radius * pop;
            if (sprites.Get("hitcircle") is { } hitCircle)
            {
                drawSprite(canvas, hitCircle, position.X, position.Y, spriteScale, colour, alpha);
                if (sprites.Get("hitcircleoverlay") is { } overlay)
                {
                    drawSprite(canvas, overlay, position.X, position.Y, spriteScale, SKColors.White, alpha);
                }
            }
            else
            {
                fill.Color = colour.WithAlpha(byteAlpha);
                canvas.DrawCircle(position.X, position.Y, radius * pop, fill);
                stroke.StrokeWidth = radius * 0.12f;
                stroke.Color = SKColors.White.WithAlpha(byteAlpha);
                canvas.DrawCircle(position.X, position.Y, radius * pop * 0.94f, stroke);
            }

            if (number > 0 && approach > 0 && !drawComboNumber(canvas, number, position, radius, alpha))
            {
                text.Color = SKColors.White.WithAlpha(byteAlpha);
                canvas.DrawText(number.ToString(CultureInfo.InvariantCulture), position.X, position.Y + text.TextSize * 0.36f, text);
            }

            if (approach > 1f)
            {
                if (sprites.Get("approachcircle") is { } approachCircle)
                {
                    drawSprite(canvas, approachCircle, position.X, position.Y, radius / sprite_base_radius * approach, colour, alpha);
                }
                else
                {
                    stroke.StrokeWidth = radius * 0.08f;
                    stroke.Color = colour.WithAlpha(byteAlpha);
                    canvas.DrawCircle(position.X, position.Y, radius * approach, stroke);
                }
            }
        }

        /// <summary>Draws the number with the skin's digit sprites; false when the skin has no digits.</summary>
        private bool drawComboNumber(SKCanvas canvas, int number, Vector2 position, float radius, float alpha)
        {
            Span<int> digits = stackalloc int[10];
            var count = 0;
            for (var remaining = Math.Abs(number); remaining > 0 || count == 0; remaining /= 10)
            {
                digits[count++] = remaining % 10;
            }

            // Digits were collected least significant first; measure them before drawing left to right.
            var totalWidth = -skin.HitCircleOverlap * (count - 1f);
            for (var i = 0; i < count; i++)
            {
                if (sprites.Digit(digits[i]) is not { } glyph)
                {
                    return false;
                }

                totalWidth += glyph.Width;
            }

            // osu! draws the digits at 0.8 of the circle's scale, overlapping by HitCircleOverlap 1x pixels.
            var scale = radius / sprite_base_radius * 0.8f;
            var x = position.X - totalWidth * scale / 2f;
            for (var i = count - 1; i >= 0; i--)
            {
                if (sprites.Digit(digits[i]) is { } glyph)
                {
                    drawSprite(canvas, glyph, x, position.Y, scale, SKColors.White, alpha, anchorX: 0f);
                    x += (glyph.Width - skin.HitCircleOverlap) * scale;
                }
            }

            return true;
        }

        private void drawSliderBody(SKCanvas canvas, PreviewObject slider, SKColor colour, float alpha, float radius, SKPaint stroke)
        {
            if (slider.Path.Count < 2 || alpha <= 0)
            {
                return;
            }

            using var path = new SKPath();
            path.MoveTo(slider.Path[0].X, slider.Path[0].Y);
            for (var i = 1; i < slider.Path.Count; i++)
            {
                path.LineTo(slider.Path[i].X, slider.Path[i].Y);
            }

            var byteAlpha = (byte)(alpha * 255);
            var border = skin.SliderBorder is { } skinBorder ? toSk(skinBorder) : SKColors.White;
            var track = skin.SliderTrackOverride is { } skinTrack ? toSk(skinTrack) : darken(colour, 0.55f);
            stroke.StrokeWidth = radius * 2f;
            stroke.Color = border.WithAlpha((byte)(byteAlpha * 0.8));
            canvas.DrawPath(path, stroke);
            stroke.StrokeWidth = radius * 1.7f;
            stroke.Color = track.WithAlpha((byte)(byteAlpha * 0.9));
            canvas.DrawPath(path, stroke);
        }

        /// <summary>The vector look's end marker, used when the skin has no reverse arrow.</summary>
        private static void drawSliderEndMarker(SKCanvas canvas, PreviewObject slider, float alpha, float radius, SKPaint stroke)
        {
            if (slider.Path.Count < 2 || alpha <= 0)
            {
                return;
            }

            var end = slider.Path[^1];
            stroke.StrokeWidth = radius * 0.1f;
            stroke.Color = SKColors.White.WithAlpha((byte)(alpha * 255 * 0.7));
            canvas.DrawCircle(end.X, end.Y, radius * 0.7f, stroke);
        }

        /// <summary>Repeat arrow at whichever end the ball bounces off next; false when the skin has none.</summary>
        private bool drawReverseArrow(SKCanvas canvas, PreviewObject slider, float alpha, float radius)
        {
            if (sprites.Get("reversearrow") is not { } arrow)
            {
                return false;
            }

            var span = spanAt(slider, time);
            if (slider.Repeats < 2 || slider.Path.Count < 2 || alpha <= 0 || span + 1 >= slider.Repeats)
            {
                return true; // nothing to bounce off, and no vector marker either
            }

            var (at, towards) = span % 2 == 0 ? (slider.Path[^1], slider.Path[^2]) : (slider.Path[0], slider.Path[1]);
            var direction = towards - at;
            var angle = MathF.Atan2(direction.Y, direction.X) * 180f / MathF.PI;
            var pulse = 1f + 0.1f * MathF.Sin((float)(time.TotalMilliseconds / 120));
            drawSprite(canvas, arrow, at.X, at.Y, radius / sprite_base_radius * pulse, SKColors.White, alpha, angle);
            return true;
        }

        private void drawSliderBall(SKCanvas canvas, PreviewObject slider, SKColor colour, float radius, SKPaint fill, SKPaint stroke)
        {
            var position = sliderPosition(slider, time);
            var frames = sprites.Frames("sliderb");
            if (frames.Count > 0)
            {
                var frame = frames[(int)(time.TotalMilliseconds / slider_ball_frame.TotalMilliseconds) % frames.Count];
                var tint = skin.AllowSliderBallTint ? colour : skin.SliderBall is { } ballColour ? toSk(ballColour) : SKColors.White;
                drawSprite(canvas, frame, position.X, position.Y, radius / sprite_base_radius, tint, 1f);
            }
            else
            {
                fill.Color = colour;
                canvas.DrawCircle(position.X, position.Y, radius * 0.9f, fill);
            }

            if (sprites.Get("sliderfollowcircle") is { } follow)
            {
                drawSprite(canvas, follow, position.X, position.Y, radius / sprite_base_radius, SKColors.White, 1f);
            }
            else
            {
                stroke.StrokeWidth = radius * 0.1f;
                stroke.Color = SKColors.White;
                canvas.DrawCircle(position.X, position.Y, radius * 1.9f, stroke);
            }
        }

        /// <summary>Which repeat span the slider is in at <paramref name="at"/>, and how far through it (0..1).</summary>
        private static (int span, double within) spanProgress(PreviewObject slider, TimeSpan at)
        {
            var duration = slider.Duration.TotalMilliseconds;
            if (duration <= 0)
            {
                return (0, 1);
            }

            var spans = Math.Max(1, slider.Repeats);
            var progress = Math.Clamp((at - slider.StartTime).TotalMilliseconds / duration, 0, 1) * spans;
            var span = Math.Min(spans - 1, (int)Math.Floor(progress));
            return (span, progress - span);
        }

        private static int spanAt(PreviewObject slider, TimeSpan at) => spanProgress(slider, at).span;

        private static Vector2 sliderPosition(PreviewObject slider, TimeSpan at)
        {
            if (slider.Path.Count == 0)
            {
                return slider.Position;
            }

            var (span, within) = spanProgress(slider, at);
            return SliderPathCalculator.PositionAt(slider.Path, span % 2 == 1 ? 1 - within : within);
        }

        private void drawSpinner(SKCanvas canvas, PreviewObject spinner, float alpha, SKPaint fill, SKPaint stroke)
        {
            if (alpha <= 0)
            {
                return;
            }

            var centre = new Vector2(playfield_width / 2, playfield_height / 2);
            var progress = spinner.Duration.TotalMilliseconds <= 0 ? 1 : Math.Clamp((time - spinner.StartTime).TotalMilliseconds / spinner.Duration.TotalMilliseconds, 0, 1);
            var spinning = time >= spinner.StartTime;
            if (sprites.Get("spinner-circle") is { } circle)
            {
                var diameter = 320f * (float)(1 - 0.4 * progress);
                var angleDegrees = spinning ? (float)(time.TotalMilliseconds * 0.36) : 0f;
                drawSprite(canvas, circle, centre.X, centre.Y, fit(circle, diameter), SKColors.White, alpha, angleDegrees);
                return;
            }

            var radius = 150f * (float)(1 - 0.75 * progress);
            var byteAlpha = (byte)(alpha * 255);
            stroke.StrokeWidth = 6;
            stroke.Color = new SKColor(255, 255, 255, (byte)(byteAlpha * 0.5));
            canvas.DrawCircle(centre.X, centre.Y, radius, stroke);
            fill.Color = new SKColor(255, 255, 255, (byte)(byteAlpha * 0.15));
            canvas.DrawCircle(centre.X, centre.Y, radius, fill);
            if (spinning)
            {
                var angle = (float)(time.TotalMilliseconds / 500 * Math.PI * 2);
                stroke.StrokeWidth = 4;
                stroke.Color = SKColors.White.WithAlpha(byteAlpha);
                canvas.DrawLine(centre.X, centre.Y, centre.X + MathF.Cos(angle) * radius, centre.Y + MathF.Sin(angle) * radius, stroke);
            }
        }

        private void drawCursor(SKCanvas canvas, float radius, SKPaint fill, SKPaint stroke)
        {
            if (cursorPosition() is not { } at)
            {
                return;
            }

            if (sprites.Get("cursor") is { } cursor)
            {
                var anchor = skin.CursorCentre ? 0.5f : 0f;
                drawSprite(canvas, cursor, at.X, at.Y, cursor_scale, SKColors.White, 1f, anchorX: anchor, anchorY: anchor);
                return;
            }

            fill.Color = new SKColor(255, 255, 255, 200);
            canvas.DrawCircle(at.X, at.Y, radius * 0.28f, fill);
            stroke.StrokeWidth = 2;
            stroke.Color = new SKColor(255, 255, 255, 120);
            canvas.DrawCircle(at.X, at.Y, radius * 0.5f, stroke);
        }

        /// <summary>Auto-play: rest on the object being hit, otherwise glide from the last object to the next.</summary>
        private Vector2? cursorPosition()
        {
            PreviewObject? previous = null;
            PreviewObject? next = null;
            foreach (var hitObject in data.Objects)
            {
                if (hitObject.Kind == PreviewObjectKind.Spinner)
                {
                    continue;
                }

                if (hitObject.StartTime <= time && time <= hitObject.EndTime)
                {
                    return hitObject.Kind == PreviewObjectKind.Slider ? sliderPosition(hitObject, time) : hitObject.Position;
                }

                if (hitObject.EndTime < time)
                {
                    previous = hitObject;
                }
                else
                {
                    next = hitObject;
                    break;
                }
            }

            if (previous is null || next is null)
            {
                return next?.Position ?? previous?.Position;
            }

            var from = previous.Kind == PreviewObjectKind.Slider ? sliderPosition(previous, previous.EndTime) : previous.Position;
            var gap = (next.StartTime - previous.EndTime).TotalMilliseconds;
            var t = gap <= 0 ? 1 : Math.Clamp((time - previous.EndTime).TotalMilliseconds / gap, 0, 1);
            var eased = (float)(t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2);
            return Vector2.Lerp(from, next.Position, eased);
        }

        // ---------------------------------------------------------------- osu!mania

        private void renderMania(SKCanvas canvas)
        {
            var columns = Math.Max(1, data.ColumnCount);
            var columnWidth = (float)Math.Min(bounds.Width / columns, 64);
            var laneWidth = columnWidth * columns;
            var left = (float)bounds.X + ((float)bounds.Width - laneWidth) / 2f;
            var top = (float)bounds.Y;
            var bottom = (float)bounds.Bottom;
            var judgeY = bottom - 48;
            var speed = (float)((judgeY - top) / lookahead.TotalMilliseconds); // px per ms

            using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            fill.Color = new SKColor(0, 0, 0, 150);
            canvas.DrawRect(left, top, laneWidth, bottom - top, fill);
            using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1, Color = new SKColor(255, 255, 255, 40) };
            for (var column = 0; column <= columns; column++)
            {
                canvas.DrawLine(left + column * columnWidth, top, left + column * columnWidth, bottom, stroke);
            }

            stroke.StrokeWidth = 3;
            stroke.Color = new SKColor(255, 255, 255, 160);
            canvas.DrawLine(left, judgeY, left + laneWidth, judgeY, stroke);

            var maniaColours = skin.ManiaFor(columns);
            foreach (var note in data.Objects)
            {
                if (note.EndTime < time || note.StartTime > time + lookahead)
                {
                    continue;
                }

                var style = mania_styles[maniaNoteStyle(note.Column, columns)];
                var x = left + note.Column * columnWidth + 3;
                var width = columnWidth - 6;
                var colour = maniaColour(note.Column, columns, maniaColours);
                var headY = judgeY - (float)(note.StartTime - time).TotalMilliseconds * speed;
                var headSprite = sprites.Get(style.Note);
                if (note.Kind == PreviewObjectKind.ManiaHold)
                {
                    var tailY = judgeY - (float)(note.EndTime - time).TotalMilliseconds * speed;
                    var visibleHead = Math.Min(headY, judgeY);
                    var bodyTop = Math.Max(top, tailY);
                    if (sprites.Get(style.HoldBody) is { } body)
                    {
                        drawImage(canvas, body, new SKRect(x, bodyTop, x + width, visibleHead), SKColors.White, 1f);
                    }
                    else
                    {
                        fill.Color = (maniaColours?.Hold is { } hold ? toSk(hold) : colour).WithAlpha(150);
                        canvas.DrawRoundRect(new SKRect(x + width * 0.2f, bodyTop, x + width * 0.8f, visibleHead), 4, 4, fill);
                    }

                    if (tailY >= top && sprites.Get(style.HoldTail) is { } tail)
                    {
                        drawSprite(canvas, tail, x + width / 2f, tailY, fitWidth(tail, width), SKColors.White, 1f);
                    }

                    headSprite = sprites.Get(style.HoldHead) ?? headSprite;
                }

                if (headY <= judgeY + 2)
                {
                    if (headSprite is not null)
                    {
                        drawSprite(canvas, headSprite, x + width / 2f, headY, fitWidth(headSprite, width), SKColors.White, 1f, anchorY: 1f);
                    }
                    else
                    {
                        fill.Color = colour;
                        canvas.DrawRoundRect(new SKRect(x, headY - 14, x + width, headY), 4, 4, fill);
                    }
                }
            }
        }

        /// <summary>osu!'s default column layout as an index into <see cref="mania_styles"/>: "1" / "2" alternating from the outside in, "S" in the middle of odd key counts.</summary>
        private static int maniaNoteStyle(int column, int columns)
        {
            if (columns % 2 == 1 && column == columns / 2)
            {
                return 2;
            }

            var fromEdge = column < columns / 2 ? column : columns - 1 - column;
            return fromEdge % 2;
        }

        private static SKColor maniaColour(int column, int columns, ManiaSkinColours? skinColours)
        {
            if (skinColours is not null && column < skinColours.Columns.Count)
            {
                return toSk(skinColours.Columns[column]);
            }

            return maniaNoteStyle(column, columns) switch
            {
                0 => new SKColor(235, 235, 245),
                1 => new SKColor(100, 160, 255),
                _ => new SKColor(255, 210, 60),
            };
        }

        // ---------------------------------------------------------------- osu!taiko

        private void renderTaiko(SKCanvas canvas)
        {
            var laneHeight = (float)Math.Min(bounds.Height * 0.4, 160);
            var centreY = (float)bounds.Y + (float)bounds.Height / 2f;
            var laneTop = centreY - laneHeight / 2f;
            var targetX = (float)bounds.X + 120;
            var right = (float)bounds.Right;
            var speed = (float)((right - targetX) / lookahead.TotalMilliseconds);
            var radius = laneHeight * 0.26f;

            using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
            fill.Color = new SKColor(0, 0, 0, 170);
            canvas.DrawRect((float)bounds.X, laneTop, (float)bounds.Width, laneHeight, fill);
            stroke.StrokeWidth = 3;
            stroke.Color = new SKColor(255, 255, 255, 120);
            canvas.DrawCircle(targetX, centreY, radius * 1.15f, stroke);

            foreach (var note in data.Objects.Reverse())
            {
                if (note.EndTime < time - fade_out || note.StartTime > time + lookahead)
                {
                    continue;
                }

                var x = targetX + (float)(note.StartTime - time).TotalMilliseconds * speed;
                var size = note.IsLarge ? radius * 1.45f : radius;
                switch (note.Kind)
                {
                    case PreviewObjectKind.TaikoDrumroll:
                        var endX = targetX + (float)(note.EndTime - time).TotalMilliseconds * speed;
                        var startX = Math.Max(targetX, x);
                        if (sprites.Get("taiko-roll-middle") is { } middle)
                        {
                            drawImage(canvas, middle, new SKRect(startX, centreY - size, endX, centreY + size), taiko_roll, 1f);
                            if (sprites.Get("taiko-roll-end") is { } end)
                            {
                                drawSprite(canvas, end, endX, centreY, fit(end, size * 2), taiko_roll, 1f, anchorX: 0f);
                            }
                        }
                        else
                        {
                            fill.Color = taiko_roll.WithAlpha(220);
                            canvas.DrawRoundRect(new SKRect(startX, centreY - size, endX, centreY + size), size, size, fill);
                        }

                        break;
                    case PreviewObjectKind.TaikoSwell:
                        if (time >= note.StartTime && time <= note.EndTime)
                        {
                            stroke.StrokeWidth = 6;
                            stroke.Color = taiko_roll.WithAlpha(200);
                            canvas.DrawCircle(targetX, centreY, radius * 2.2f, stroke);
                        }
                        else if (time < note.StartTime)
                        {
                            drawTaikoCircle(canvas, x, centreY, radius * 1.2f, taiko_roll, false, fill, stroke);
                        }

                        break;
                    default:
                        if (time <= note.StartTime)
                        {
                            drawTaikoCircle(canvas, x, centreY, size, note.Kind == PreviewObjectKind.TaikoKat ? taiko_kat : taiko_don, note.IsLarge, fill, stroke);
                        }

                        break;
                }
            }
        }

        private void drawTaikoCircle(SKCanvas canvas, float x, float y, float size, SKColor colour, bool large, SKPaint fill, SKPaint stroke)
        {
            if (drawPair(canvas, large ? taiko_large : taiko_small, x, y, size * 2, colour, 1f))
            {
                return;
            }

            fill.Color = colour;
            canvas.DrawCircle(x, y, size, fill);
            stroke.StrokeWidth = size * 0.14f;
            stroke.Color = SKColors.White;
            canvas.DrawCircle(x, y, size * 0.92f, stroke);
        }

        // ---------------------------------------------------------------- osu!catch

        private void renderCatch(SKCanvas canvas)
        {
            var scale = (float)Math.Min(bounds.Width / playfield_width, bounds.Height / playfield_height);
            var originX = (float)bounds.X + ((float)bounds.Width - playfield_width * scale) / 2f;
            var fieldHeight = (float)bounds.Height / scale;
            canvas.Save();
            canvas.Translate(originX, (float)bounds.Y);
            canvas.Scale(scale);

            var catcherY = fieldHeight - 30;
            var fallTime = data.Preempt.TotalMilliseconds;
            var speed = (float)(catcherY / fallTime);
            var radius = (float)data.CircleRadius * 0.6f;
            using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2, Color = new SKColor(255, 255, 255, 60) };
            canvas.DrawRect(0, 0, playfield_width, fieldHeight, stroke);

            float? catcherX = null;
            foreach (var fruit in data.Objects)
            {
                if (fruit.EndTime < time - fade_out || fruit.StartTime > time + data.Preempt)
                {
                    continue;
                }

                var colour = comboColour(fruit.ComboColourIndex);
                var fruitSprite = fruits[Math.Abs(fruit.ComboNumber) % fruits.Length];
                switch (fruit.Kind)
                {
                    case PreviewObjectKind.CatchJuiceStream:
                        for (var at = fruit.StartTime; at <= fruit.EndTime; at += TimeSpan.FromMilliseconds(100))
                        {
                            var position = sliderPosition(fruit, at);
                            var isHead = at == fruit.StartTime;
                            drawFruit(canvas, position.X, at, radius * (isHead ? 1f : 0.5f), colour, isHead ? fruitSprite : fruit_drop, catcherY, speed, fill, ref catcherX);
                        }

                        break;
                    case PreviewObjectKind.CatchBananaShower:
                        for (var at = fruit.StartTime; at <= fruit.EndTime; at += TimeSpan.FromMilliseconds(80))
                        {
                            var pseudoRandom = (float)((at.TotalMilliseconds * 7919) % 512);
                            drawFruit(canvas, pseudoRandom, at, radius * 0.7f, banana, fruit_bananas, catcherY, speed, fill, ref catcherX, spriteTint: SKColors.White);
                        }

                        break;
                    default:
                        drawFruit(canvas, fruit.Position.X, fruit.StartTime, radius, colour, fruitSprite, catcherY, speed, fill, ref catcherX);
                        break;
                }
            }

            var plateX = catcherX ?? playfield_width / 2;
            if (sprites.Get("fruit-catcher-idle") is { } catcher)
            {
                drawSprite(canvas, catcher, plateX, catcherY + 10, fitWidth(catcher, 100), SKColors.White, 1f, anchorY: 1f);
            }
            else
            {
                fill.Color = new SKColor(255, 255, 255, 220);
                canvas.DrawRoundRect(new SKRect(plateX - 40, catcherY, plateX + 40, catcherY + 10), 5, 5, fill);
            }

            canvas.Restore();
        }

        /// <summary>Draws one falling object; the vector fallback uses <paramref name="colour"/>, the sprite <paramref name="spriteTint"/> (defaulting to the same colour).</summary>
        private void drawFruit(SKCanvas canvas, float x, TimeSpan hitTime, float radius, SKColor colour, SpritePair sprite, float catcherY, float speed, SKPaint fill, ref float? catcherX, SKColor? spriteTint = null)
        {
            var untilHit = (hitTime - time).TotalMilliseconds;
            if (untilHit < 0)
            {
                return;
            }

            var y = catcherY - (float)untilHit * speed;
            if (y < -radius)
            {
                return;
            }

            if (!drawPair(canvas, sprite, x, y, radius * 2, spriteTint ?? colour, 1f))
            {
                fill.Color = colour;
                canvas.DrawCircle(x, y, radius, fill);
            }

            catcherX ??= x; // the next fruit to land steers the auto-catcher
        }

        // ---------------------------------------------------------------- helpers

        private IEnumerable<PreviewObject> visible(TimeSpan before, TimeSpan after)
        {
            foreach (var hitObject in data.Objects)
            {
                if (hitObject.EndTime + after < time)
                {
                    continue;
                }

                if (hitObject.StartTime - before > time)
                {
                    yield break;
                }

                yield return hitObject;
            }
        }

        /// <summary>
        /// The beatmap's colours by default, the skin's when the beatmap has none or the user
        /// prefers the skin, and osu!'s default palette when neither says anything.
        /// </summary>
        private SKColor comboColour(int index)
        {
            var palette = skin.ComboColours.Count > 0 && (preferSkinColours || data.ComboColours.Count == 0)
                ? skin.ComboColours
                : data.ComboColours.Count > 0 ? data.ComboColours : PlayfieldPreviewBuilder.DefaultComboColours;
            return toSk(palette[Math.Abs(index) % palette.Count]);
        }

        private static SKColor toSk(PreviewComboColour colour) => new(colour.R, colour.G, colour.B);

        private static SKColor darken(SKColor colour, float factor) =>
            new((byte)(colour.Red * factor), (byte)(colour.Green * factor), (byte)(colour.Blue * factor), colour.Alpha);

        /// <summary>A tinted element and its untinted overlay, following osu!'s "name" / "name-overlay" convention unless the overlay is named explicitly.</summary>
        private sealed record SpritePair(string Name, string Overlay)
        {
            public SpritePair(string name)
                : this(name, name + "-overlay")
            {
            }
        }

        /// <summary>The four elements of one mania column style ("1", "2" or "S").</summary>
        private sealed record ManiaNoteSprites(string Note, string HoldHead, string HoldBody, string HoldTail)
        {
            public ManiaNoteSprites(string style)
                : this($"mania-note{style}", $"mania-note{style}H", $"mania-note{style}L", $"mania-note{style}T")
            {
            }
        }
    }
}
