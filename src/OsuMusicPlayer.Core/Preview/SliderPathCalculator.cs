using System.Numerics;

namespace OsuMusicPlayer.Core.Preview;

public enum SliderCurveKind
{
    Linear,
    Bezier,
    PerfectCircle,
    Catmull,
}

/// <summary>
/// Expands osu! slider control points into a dense polyline the way the game does: piecewise
/// Bézier segments split at repeated points, circular arcs through three points, Catmull-Rom
/// splines, and straight lines. The result is trimmed to the slider's pixel length.
/// </summary>
public static class SliderPathCalculator
{
    private const float bezier_tolerance = 0.25f;
    private const float circle_tolerance = 0.1f;
    private const int catmull_detail = 50;

    public static IReadOnlyList<Vector2> Calculate(SliderCurveKind kind, IReadOnlyList<Vector2> controlPoints, double pixelLength)
    {
        ArgumentNullException.ThrowIfNull(controlPoints);
        if (controlPoints.Count == 0)
        {
            return [];
        }

        if (controlPoints.Count == 1)
        {
            return [controlPoints[0]];
        }

        var points = kind switch
        {
            SliderCurveKind.Linear => controlPoints.ToList(),
            SliderCurveKind.PerfectCircle when controlPoints.Count == 3 => perfectCircle(controlPoints[0], controlPoints[1], controlPoints[2]),
            SliderCurveKind.Catmull => catmull(controlPoints),
            _ => bezier(controlPoints),
        };

        return trim(points, pixelLength);
    }

    /// <summary>Length along the polyline.</summary>
    public static double Length(IReadOnlyList<Vector2> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        double length = 0;
        for (var i = 1; i < path.Count; i++)
        {
            length += Vector2.Distance(path[i - 1], path[i]);
        }

        return length;
    }

    /// <summary>The point at <paramref name="progress"/> (0..1) of the path length.</summary>
    public static Vector2 PositionAt(IReadOnlyList<Vector2> path, double progress)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count == 0)
        {
            return Vector2.Zero;
        }

        var target = Math.Clamp(progress, 0, 1) * Length(path);
        double walked = 0;
        for (var i = 1; i < path.Count; i++)
        {
            var segment = Vector2.Distance(path[i - 1], path[i]);
            if (walked + segment >= target)
            {
                var t = segment <= 0 ? 0 : (float)((target - walked) / segment);
                return Vector2.Lerp(path[i - 1], path[i], t);
            }

            walked += segment;
        }

        return path[^1];
    }

    private static List<Vector2> trim(List<Vector2> points, double pixelLength)
    {
        if (pixelLength <= 0 || points.Count < 2)
        {
            return points;
        }

        var result = new List<Vector2> { points[0] };
        double walked = 0;
        for (var i = 1; i < points.Count; i++)
        {
            var segment = Vector2.Distance(points[i - 1], points[i]);
            if (walked + segment >= pixelLength)
            {
                var t = segment <= 0 ? 0 : (float)((pixelLength - walked) / segment);
                result.Add(Vector2.Lerp(points[i - 1], points[i], t));
                return result;
            }

            walked += segment;
            result.Add(points[i]);
        }

        return result;
    }

    private static List<Vector2> bezier(IReadOnlyList<Vector2> controlPoints)
    {
        var output = new List<Vector2>();
        var segment = new List<Vector2>();
        for (var i = 0; i < controlPoints.Count; i++)
        {
            segment.Add(controlPoints[i]);
            var last = i == controlPoints.Count - 1;
            if (!last && controlPoints[i] != controlPoints[i + 1])
            {
                continue;
            }

            approximateBezier(segment, output);
            segment.Clear();
            if (!last)
            {
                segment.Add(controlPoints[i]);
            }
        }

        return output;
    }

    private static void approximateBezier(List<Vector2> segment, List<Vector2> output)
    {
        if (segment.Count == 0)
        {
            return;
        }

        if (segment.Count == 1)
        {
            output.Add(segment[0]);
            return;
        }

        // Sample density from the control polygon length keeps long sliders smooth.
        double polygon = 0;
        for (var i = 1; i < segment.Count; i++)
        {
            polygon += Vector2.Distance(segment[i - 1], segment[i]);
        }

        var steps = Math.Clamp((int)Math.Ceiling(polygon / bezier_tolerance / 4), 8, 512);
        var buffer = new Vector2[segment.Count];
        for (var step = 0; step <= steps; step++)
        {
            var t = (float)step / steps;
            segment.CopyTo(buffer);
            for (var level = segment.Count - 1; level > 0; level--)
            {
                for (var i = 0; i < level; i++)
                {
                    buffer[i] = Vector2.Lerp(buffer[i], buffer[i + 1], t);
                }
            }

            if (output.Count == 0 || output[^1] != buffer[0])
            {
                output.Add(buffer[0]);
            }
        }
    }

    private static List<Vector2> perfectCircle(Vector2 a, Vector2 b, Vector2 c)
    {
        var aSq = (b - c).LengthSquared();
        var bSq = (a - c).LengthSquared();
        var cSq = (a - b).LengthSquared();
        if (aSq < 1e-3 || bSq < 1e-3 || cSq < 1e-3)
        {
            return bezier([a, b, c]);
        }

        var s = aSq * (bSq + cSq - aSq);
        var t = bSq * (aSq + cSq - bSq);
        var u = cSq * (aSq + bSq - cSq);
        var sum = s + t + u;
        if (Math.Abs(sum) < 1e-3)
        {
            return [a, b, c]; // collinear: a straight line through the points
        }

        var centre = (s * a + t * b + u * c) / sum;
        var dA = a - centre;
        var dC = c - centre;
        var radius = dA.Length();
        var dB = b - centre;
        var thetaStart = Math.Atan2(dA.Y, dA.X);
        var counterClockwise = normalizeAngle(Math.Atan2(dC.Y, dC.X) - thetaStart);
        var middle = normalizeAngle(Math.Atan2(dB.Y, dB.X) - thetaStart);

        // Walk in whichever direction passes through the middle control point.
        var direction = middle <= counterClockwise ? 1.0 : -1.0;
        var range = direction > 0 ? counterClockwise : 2 * Math.PI - counterClockwise;

        var amount = range <= 0 ? 2 : Math.Max(2, (int)Math.Ceiling(range / (2 * Math.Acos(1 - circle_tolerance / radius))));
        var output = new List<Vector2>(amount + 1);
        for (var i = 0; i <= amount; i++)
        {
            var fraction = (double)i / amount;
            var theta = thetaStart + direction * fraction * range;
            output.Add(centre + new Vector2((float)(Math.Cos(theta) * radius), (float)(Math.Sin(theta) * radius)));
        }

        // Pin the ends to the control points so rounding never moves the head or tail.
        output[0] = a;
        output[^1] = c;
        return output;
    }

    private static double normalizeAngle(double angle)
    {
        while (angle < 0)
        {
            angle += 2 * Math.PI;
        }

        while (angle >= 2 * Math.PI)
        {
            angle -= 2 * Math.PI;
        }

        return angle;
    }

    private static List<Vector2> catmull(IReadOnlyList<Vector2> controlPoints)
    {
        var output = new List<Vector2>();
        for (var i = 0; i < controlPoints.Count - 1; i++)
        {
            var v1 = i > 0 ? controlPoints[i - 1] : controlPoints[i];
            var v2 = controlPoints[i];
            var v3 = i < controlPoints.Count - 1 ? controlPoints[i + 1] : v2 + v2 - v1;
            var v4 = i < controlPoints.Count - 2 ? controlPoints[i + 2] : v3 + v3 - v2;
            for (var c = 0; c < catmull_detail; c++)
            {
                output.Add(catmullPoint(v1, v2, v3, v4, (float)c / catmull_detail));
            }
        }

        output.Add(controlPoints[^1]);
        return output;
    }

    private static Vector2 catmullPoint(Vector2 v1, Vector2 v2, Vector2 v3, Vector2 v4, float t)
    {
        var t2 = t * t;
        var t3 = t * t2;
        return new Vector2(
            0.5f * (2f * v2.X + (-v1.X + v3.X) * t + (2f * v1.X - 5f * v2.X + 4f * v3.X - v4.X) * t2 + (-v1.X + 3f * v2.X - 3f * v3.X + v4.X) * t3),
            0.5f * (2f * v2.Y + (-v1.Y + v3.Y) * t + (2f * v1.Y - 5f * v2.Y + 4f * v3.Y - v4.Y) * t2 + (-v1.Y + 3f * v2.Y - 3f * v3.Y + v4.Y) * t3));
    }
}
