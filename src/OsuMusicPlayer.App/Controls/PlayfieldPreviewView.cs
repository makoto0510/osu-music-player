using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using OsuMusicPlayer.Core.Models;
using OsuMusicPlayer.Core.Preview;
using SkiaSharp;

namespace OsuMusicPlayer.App.Controls;

/// <summary>
/// Draws a <see cref="PlayfieldPreviewData"/> as an auto-play, one frame per animation tick,
/// at the time returned by <see cref="Clock"/>. osu!, taiko, catch and mania each get a
/// simplified rendition of their playfield.
/// </summary>
public sealed class PlayfieldPreviewView : Control
{
    public static readonly StyledProperty<PlayfieldPreviewData?> DataProperty =
        AvaloniaProperty.Register<PlayfieldPreviewView, PlayfieldPreviewData?>(nameof(Data));

    private bool frameRequested;

    static PlayfieldPreviewView()
    {
        AffectsRender<PlayfieldPreviewView>(DataProperty);
    }

    public PlayfieldPreviewData? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
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
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Data is not { } data || Clock is not { } clock || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        context.Custom(new PreviewDrawOperation(new Rect(Bounds.Size), data, clock()));
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

    private sealed class PreviewDrawOperation(Rect bounds, PlayfieldPreviewData data, TimeSpan time) : ICustomDrawOperation
    {
        private const float playfield_width = 512f;
        private const float playfield_height = 384f;
        private static readonly TimeSpan fade_out = TimeSpan.FromMilliseconds(240);
        private static readonly TimeSpan lookahead = TimeSpan.FromMilliseconds(1000);

        public Rect Bounds => bounds;

        public void Dispose()
        {
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
            fill.Color = colour.WithAlpha(byteAlpha);
            canvas.DrawCircle(position.X, position.Y, radius * pop, fill);
            stroke.StrokeWidth = radius * 0.12f;
            stroke.Color = SKColors.White.WithAlpha(byteAlpha);
            canvas.DrawCircle(position.X, position.Y, radius * pop * 0.94f, stroke);
            if (number > 0 && approach > 0)
            {
                text.Color = SKColors.White.WithAlpha(byteAlpha);
                canvas.DrawText(number.ToString(System.Globalization.CultureInfo.InvariantCulture), position.X, position.Y + text.TextSize * 0.36f, text);
            }

            if (approach > 1f)
            {
                stroke.StrokeWidth = radius * 0.08f;
                stroke.Color = colour.WithAlpha(byteAlpha);
                canvas.DrawCircle(position.X, position.Y, radius * approach, stroke);
            }
        }

        private static void drawSliderBody(SKCanvas canvas, PreviewObject slider, SKColor colour, float alpha, float radius, SKPaint stroke)
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
            stroke.StrokeWidth = radius * 2f;
            stroke.Color = SKColors.White.WithAlpha((byte)(byteAlpha * 0.8));
            canvas.DrawPath(path, stroke);
            stroke.StrokeWidth = radius * 1.7f;
            stroke.Color = darken(colour, 0.55f).WithAlpha((byte)(byteAlpha * 0.9));
            canvas.DrawPath(path, stroke);

            // Repeat arrow / end marker.
            var end = slider.Path[^1];
            stroke.StrokeWidth = radius * 0.1f;
            stroke.Color = SKColors.White.WithAlpha((byte)(byteAlpha * 0.7));
            canvas.DrawCircle(end.X, end.Y, radius * 0.7f, stroke);
        }

        private void drawSliderBall(SKCanvas canvas, PreviewObject slider, SKColor colour, float radius, SKPaint fill, SKPaint stroke)
        {
            var position = sliderPosition(slider, time);
            fill.Color = colour;
            canvas.DrawCircle(position.X, position.Y, radius * 0.9f, fill);
            stroke.StrokeWidth = radius * 0.1f;
            stroke.Color = SKColors.White;
            canvas.DrawCircle(position.X, position.Y, radius * 1.9f, stroke);
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
            var radius = 150f * (float)(1 - 0.75 * progress);
            var byteAlpha = (byte)(alpha * 255);
            stroke.StrokeWidth = 6;
            stroke.Color = new SKColor(255, 255, 255, (byte)(byteAlpha * 0.5));
            canvas.DrawCircle(centre.X, centre.Y, radius, stroke);
            fill.Color = new SKColor(255, 255, 255, (byte)(byteAlpha * 0.15));
            canvas.DrawCircle(centre.X, centre.Y, radius, fill);
            if (time >= spinner.StartTime)
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

            foreach (var note in data.Objects)
            {
                if (note.EndTime < time || note.StartTime > time + lookahead)
                {
                    continue;
                }

                var x = left + note.Column * columnWidth + 3;
                var width = columnWidth - 6;
                var colour = maniaColour(note.Column, columns);
                var headY = judgeY - (float)(note.StartTime - time).TotalMilliseconds * speed;
                if (note.Kind == PreviewObjectKind.ManiaHold)
                {
                    var tailY = judgeY - (float)(note.EndTime - time).TotalMilliseconds * speed;
                    var visibleHead = Math.Min(headY, judgeY);
                    fill.Color = colour.WithAlpha(150);
                    canvas.DrawRoundRect(new SKRect(x + width * 0.2f, Math.Max(top, tailY), x + width * 0.8f, visibleHead), 4, 4, fill);
                }

                if (headY <= judgeY + 2)
                {
                    fill.Color = colour;
                    canvas.DrawRoundRect(new SKRect(x, headY - 14, x + width, headY), 4, 4, fill);
                }
            }
        }

        private static SKColor maniaColour(int column, int columns)
        {
            if (columns % 2 == 1 && column == columns / 2)
            {
                return new SKColor(255, 210, 60);
            }

            return column % 2 == 0 ? new SKColor(235, 235, 245) : new SKColor(100, 160, 255);
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
                        fill.Color = new SKColor(255, 200, 40, 220);
                        canvas.DrawRoundRect(new SKRect(Math.Max(targetX, x), centreY - size, endX, centreY + size), size, size, fill);
                        break;
                    case PreviewObjectKind.TaikoSwell:
                        if (time >= note.StartTime && time <= note.EndTime)
                        {
                            stroke.StrokeWidth = 6;
                            stroke.Color = new SKColor(255, 200, 40, 200);
                            canvas.DrawCircle(targetX, centreY, radius * 2.2f, stroke);
                        }
                        else if (time < note.StartTime)
                        {
                            fill.Color = new SKColor(255, 200, 40, 200);
                            canvas.DrawCircle(x, centreY, radius * 1.2f, fill);
                        }

                        break;
                    default:
                        if (time > note.StartTime)
                        {
                            break; // hit
                        }

                        fill.Color = note.Kind == PreviewObjectKind.TaikoKat ? new SKColor(67, 142, 173) : new SKColor(235, 69, 43);
                        canvas.DrawCircle(x, centreY, size, fill);
                        stroke.StrokeWidth = size * 0.14f;
                        stroke.Color = SKColors.White;
                        canvas.DrawCircle(x, centreY, size * 0.92f, stroke);
                        break;
                }
            }
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
                switch (fruit.Kind)
                {
                    case PreviewObjectKind.CatchJuiceStream:
                        for (var at = fruit.StartTime; at <= fruit.EndTime; at += TimeSpan.FromMilliseconds(100))
                        {
                            var position = sliderPosition(fruit, at);
                            drawFruit(canvas, position.X, at, radius * (at == fruit.StartTime ? 1f : 0.5f), colour, catcherY, speed, fill, ref catcherX);
                        }

                        break;
                    case PreviewObjectKind.CatchBananaShower:
                        for (var at = fruit.StartTime; at <= fruit.EndTime; at += TimeSpan.FromMilliseconds(80))
                        {
                            var pseudoRandom = (float)((at.TotalMilliseconds * 7919) % 512);
                            drawFruit(canvas, pseudoRandom, at, radius * 0.7f, new SKColor(255, 230, 60), catcherY, speed, fill, ref catcherX);
                        }

                        break;
                    default:
                        drawFruit(canvas, fruit.Position.X, fruit.StartTime, radius, colour, catcherY, speed, fill, ref catcherX);
                        break;
                }
            }

            var plateX = catcherX ?? playfield_width / 2;
            fill.Color = new SKColor(255, 255, 255, 220);
            canvas.DrawRoundRect(new SKRect(plateX - 40, catcherY, plateX + 40, catcherY + 10), 5, 5, fill);
            canvas.Restore();
        }

        private void drawFruit(SKCanvas canvas, float x, TimeSpan hitTime, float radius, SKColor colour, float catcherY, float speed, SKPaint fill, ref float? catcherX)
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

            fill.Color = colour;
            canvas.DrawCircle(x, y, radius, fill);
            if (catcherX is null)
            {
                catcherX = x; // the next fruit to land steers the auto-catcher
            }
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

        private SKColor comboColour(int index)
        {
            if (data.ComboColours.Count == 0)
            {
                return new SKColor(255, 192, 0);
            }

            var colour = data.ComboColours[Math.Abs(index) % data.ComboColours.Count];
            return new SKColor(colour.R, colour.G, colour.B);
        }

        private static SKColor darken(SKColor colour, float factor) =>
            new((byte)(colour.Red * factor), (byte)(colour.Green * factor), (byte)(colour.Blue * factor), colour.Alpha);
    }
}
