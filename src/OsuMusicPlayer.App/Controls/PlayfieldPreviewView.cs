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
            var skin = change.GetNewValue<PreviewSkin?>() ?? PreviewSkin.Default;
            sprites = skin.IsDefault ? SkinSprites.Default : new SkinSprites(skin);
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
        private static readonly string[] fruit_names = ["pear", "grapes", "apple", "orange"];
        private static readonly SKColor taiko_don = new(235, 69, 43);
        private static readonly SKColor taiko_kat = new(67, 142, 173);
        private static readonly SKColor taiko_roll = new(255, 200, 40);

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
        /// Draws a skin element centred on (x, y). <paramref name="scale"/> converts 1x skin pixels
        /// to canvas units; the tint multiplies the image (white leaves it untouched).
        /// </summary>
        private void drawSprite(SKCanvas canvas, SkinSprites.Sprite sprite, float x, float y, float scale, SKColor tint, float alpha, float rotationDegrees = 0, float anchorX = 0.5f, float anchorY = 0.5f)
        {
            if (alpha <= 0)
            {
                return;
            }

            var paint = spritePaint ??= new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.Medium };
            paint.Color = SKColors.White.WithAlpha((byte)(Math.Clamp(alpha, 0, 1) * 255));
            using var filter = tint == SKColors.White ? null : SKColorFilter.CreateBlendMode(tint, SKBlendMode.Modulate);
            paint.ColorFilter = filter;

            var width = sprite.Width * scale;
            var height = sprite.Height * scale;
            canvas.Save();
            canvas.Translate(x, y);
            if (rotationDegrees != 0)
            {
                canvas.RotateDegrees(rotationDegrees);
            }

            canvas.DrawImage(sprite.Image, new SKRect(-width * anchorX, -height * anchorY, width * (1 - anchorX), height * (1 - anchorY)), paint);
            canvas.Restore();
            paint.ColorFilter = null;
        }

        /// <summary>Stretches a skin element over an exact rectangle (hold bodies, drumrolls).</summary>
        private void drawSpriteStretched(SKCanvas canvas, SkinSprites.Sprite sprite, SKRect destination, SKColor tint, float alpha)
        {
            if (alpha <= 0 || destination.Width <= 0 || destination.Height <= 0)
            {
                return;
            }

            var paint = spritePaint ??= new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.Medium };
            paint.Color = SKColors.White.WithAlpha((byte)(Math.Clamp(alpha, 0, 1) * 255));
            using var filter = tint == SKColors.White ? null : SKColorFilter.CreateBlendMode(tint, SKBlendMode.Modulate);
            paint.ColorFilter = filter;
            canvas.DrawImage(sprite.Image, destination, paint);
            paint.ColorFilter = null;
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
                        drawReverseArrow(canvas, hitObject, alpha, radius);
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
            var digits = number.ToString(CultureInfo.InvariantCulture);
            var glyphs = new SkinSprites.Sprite[digits.Length];
            for (var i = 0; i < digits.Length; i++)
            {
                if (sprites.Get($"{skin.HitCirclePrefix}-{digits[i]}") is not { } glyph)
                {
                    return false;
                }

                glyphs[i] = glyph;
            }

            // osu! draws the digits at 0.8 of the circle's scale, overlapping by HitCircleOverlap 1x pixels.
            var scale = radius / sprite_base_radius * 0.8f;
            var totalWidth = glyphs.Sum(static glyph => glyph.Width) - skin.HitCircleOverlap * (glyphs.Length - 1);
            var x = position.X - totalWidth * scale / 2f;
            foreach (var glyph in glyphs)
            {
                drawSprite(canvas, glyph, x, position.Y, scale, SKColors.White, alpha, anchorX: 0f);
                x += (glyph.Width - skin.HitCircleOverlap) * scale;
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

            if (sprites.Get("reversearrow") is null)
            {
                // End marker (the skinned look uses reversearrow instead).
                var end = slider.Path[^1];
                stroke.StrokeWidth = radius * 0.1f;
                stroke.Color = SKColors.White.WithAlpha((byte)(byteAlpha * 0.7));
                canvas.DrawCircle(end.X, end.Y, radius * 0.7f, stroke);
            }
        }

        /// <summary>Repeat arrow at whichever end the ball bounces off next.</summary>
        private void drawReverseArrow(SKCanvas canvas, PreviewObject slider, float alpha, float radius)
        {
            if (slider.Repeats < 2 || slider.Path.Count < 2 || alpha <= 0 || sprites.Get("reversearrow") is not { } arrow)
            {
                return;
            }

            var span = currentSpan(slider);
            if (span + 1 >= slider.Repeats)
            {
                return; // last span: no more bounces
            }

            Vector2 at, towards;
            if (span % 2 == 0)
            {
                at = slider.Path[^1];
                towards = slider.Path[^2];
            }
            else
            {
                at = slider.Path[0];
                towards = slider.Path[1];
            }

            var direction = towards - at;
            var angle = MathF.Atan2(direction.Y, direction.X) * 180f / MathF.PI;
            var pulse = 1f + 0.1f * MathF.Sin((float)(time.TotalMilliseconds / 120));
            drawSprite(canvas, arrow, at.X, at.Y, radius / sprite_base_radius * pulse, SKColors.White, alpha, angle);
        }

        private int currentSpan(PreviewObject slider)
        {
            var duration = slider.Duration.TotalMilliseconds;
            if (duration <= 0 || time < slider.StartTime)
            {
                return 0;
            }

            var spans = Math.Max(1, slider.Repeats);
            var progress = Math.Clamp((time - slider.StartTime).TotalMilliseconds / duration, 0, 1) * spans;
            return Math.Min(spans - 1, (int)Math.Floor(progress));
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

        private static Vector2 sliderPosition(PreviewObject slider, TimeSpan at)
        {
            if (slider.Path.Count == 0)
            {
                return slider.Position;
            }

            var duration = slider.Duration.TotalMilliseconds;
            if (duration <= 0)
            {
                return slider.Path[^1];
            }

            var spans = Math.Max(1, slider.Repeats);
            var progress = Math.Clamp((at - slider.StartTime).TotalMilliseconds / duration, 0, 1) * spans;
            var span = Math.Min(spans - 1, (int)Math.Floor(progress));
            var within = progress - span;
            if (span % 2 == 1)
            {
                within = 1 - within;
            }

            return SliderPathCalculator.PositionAt(slider.Path, within);
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
            var angleDegrees = spinning ? (float)(time.TotalMilliseconds * 0.36) : 0f;
            if (sprites.Get("spinner-circle") is { } circle)
            {
                var diameter = 320f * (float)(1 - 0.4 * progress);
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
            var position = cursorPosition();
            if (position is not { } at)
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

                var style = maniaNoteStyle(note.Column, columns);
                var x = left + note.Column * columnWidth + 3;
                var width = columnWidth - 6;
                var colour = maniaColour(note.Column, columns, maniaColours);
                var headY = judgeY - (float)(note.StartTime - time).TotalMilliseconds * speed;
                var noteSprite = sprites.Get($"mania-note{style}");
                if (note.Kind == PreviewObjectKind.ManiaHold)
                {
                    var tailY = judgeY - (float)(note.EndTime - time).TotalMilliseconds * speed;
                    var visibleHead = Math.Min(headY, judgeY);
                    var bodyTop = Math.Max(top, tailY);
                    if (sprites.Get($"mania-note{style}L") is { } body)
                    {
                        drawSpriteStretched(canvas, body, new SKRect(x, bodyTop, x + width, visibleHead), SKColors.White, 1f);
                    }
                    else
                    {
                        fill.Color = (maniaColours?.Hold is { } hold ? toSk(hold) : colour).WithAlpha(150);
                        canvas.DrawRoundRect(new SKRect(x + width * 0.2f, bodyTop, x + width * 0.8f, visibleHead), 4, 4, fill);
                    }

                    if (tailY >= top && sprites.Get($"mania-note{style}T") is { } tail)
                    {
                        drawSprite(canvas, tail, x + width / 2f, tailY, fitWidth(tail, width), SKColors.White, 1f, anchorY: 0.5f);
                    }

                    noteSprite = sprites.Get($"mania-note{style}H") ?? noteSprite;
                }

                if (headY <= judgeY + 2)
                {
                    if (noteSprite is not null)
                    {
                        drawSprite(canvas, noteSprite, x + width / 2f, headY, fitWidth(noteSprite, width), SKColors.White, 1f, anchorY: 1f);
                    }
                    else
                    {
                        fill.Color = colour;
                        canvas.DrawRoundRect(new SKRect(x, headY - 14, x + width, headY), 4, 4, fill);
                    }
                }
            }
        }

        /// <summary>osu!'s default column layout: "1" / "2" alternating from the outside in, "S" in the middle of odd key counts.</summary>
        private static string maniaNoteStyle(int column, int columns)
        {
            if (columns % 2 == 1 && column == columns / 2)
            {
                return "S";
            }

            var fromEdge = column < columns / 2 ? column : columns - 1 - column;
            return fromEdge % 2 == 0 ? "1" : "2";
        }

        private static SKColor maniaColour(int column, int columns, ManiaSkinColours? skinColours)
        {
            if (skinColours is not null && column < skinColours.Columns.Count)
            {
                return toSk(skinColours.Columns[column]);
            }

            if (columns % 2 == 1 && column == columns / 2)
            {
                return new SKColor(255, 210, 60);
            }

            return maniaNoteStyle(column, columns) == "1" ? new SKColor(235, 235, 245) : new SKColor(100, 160, 255);
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
                            drawSpriteStretched(canvas, middle, new SKRect(startX, centreY - size, endX, centreY + size), taiko_roll, 1f);
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
                        if (time > note.StartTime)
                        {
                            break; // hit
                        }

                        drawTaikoCircle(canvas, x, centreY, size, note.Kind == PreviewObjectKind.TaikoKat ? taiko_kat : taiko_don, note.IsLarge, fill, stroke);
                        break;
                }
            }
        }

        private void drawTaikoCircle(SKCanvas canvas, float x, float y, float size, SKColor colour, bool large, SKPaint fill, SKPaint stroke)
        {
            var baseName = large ? "taikobigcircle" : "taikohitcircle";
            if (sprites.Get(baseName) is { } circle)
            {
                var scale = fit(circle, size * 2);
                drawSprite(canvas, circle, x, y, scale, colour, 1f);
                if (sprites.Get(baseName + "overlay") is { } overlay)
                {
                    drawSprite(canvas, overlay, x, y, scale, SKColors.White, 1f);
                }

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
                var fruitName = "fruit-" + fruit_names[Math.Abs(fruit.ComboNumber) % fruit_names.Length];
                switch (fruit.Kind)
                {
                    case PreviewObjectKind.CatchJuiceStream:
                        for (var at = fruit.StartTime; at <= fruit.EndTime; at += TimeSpan.FromMilliseconds(100))
                        {
                            var position = sliderPosition(fruit, at);
                            var isHead = at == fruit.StartTime;
                            drawFruit(canvas, position.X, at, radius * (isHead ? 1f : 0.5f), colour, isHead ? fruitName : "fruit-drop", catcherY, speed, fill, ref catcherX);
                        }

                        break;
                    case PreviewObjectKind.CatchBananaShower:
                        for (var at = fruit.StartTime; at <= fruit.EndTime; at += TimeSpan.FromMilliseconds(80))
                        {
                            var pseudoRandom = (float)((at.TotalMilliseconds * 7919) % 512);
                            drawFruit(canvas, pseudoRandom, at, radius * 0.7f, new SKColor(255, 230, 60), "fruit-bananas", catcherY, speed, fill, ref catcherX, tintSprite: false);
                        }

                        break;
                    default:
                        drawFruit(canvas, fruit.Position.X, fruit.StartTime, radius, colour, fruitName, catcherY, speed, fill, ref catcherX);
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

        private void drawFruit(SKCanvas canvas, float x, TimeSpan hitTime, float radius, SKColor colour, string spriteName, float catcherY, float speed, SKPaint fill, ref float? catcherX, bool tintSprite = true)
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

            if (sprites.Get(spriteName) is { } sprite)
            {
                var scale = fit(sprite, radius * 2);
                drawSprite(canvas, sprite, x, y, scale, tintSprite ? colour : SKColors.White, 1f);
                if (sprites.Get(spriteName + "-overlay") is { } overlay)
                {
                    drawSprite(canvas, overlay, x, y, scale, SKColors.White, 1f);
                }
            }
            else
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
            var palette = skin.ComboColours.Count > 0 && (preferSkinColours || !data.HasBeatmapComboColours)
                ? skin.ComboColours
                : data.ComboColours;
            if (palette.Count == 0)
            {
                return new SKColor(255, 192, 0);
            }

            return toSk(palette[Math.Abs(index) % palette.Count]);
        }

        private static SKColor toSk(PreviewComboColour colour) => new(colour.R, colour.G, colour.B);

        private static SKColor darken(SKColor colour, float factor) =>
            new((byte)(colour.Red * factor), (byte)(colour.Green * factor), (byte)(colour.Blue * factor), colour.Alpha);
    }
}
