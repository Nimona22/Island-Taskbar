using System;
using System.Collections.Generic;

namespace IslandTaskbar.Models
{
    internal readonly record struct IslandSurfacePoint(double X, double Y);

    /// <summary>Shared sampled outline for the selected top-attached notch, without native handles.</summary>
    internal static class IslandSurfaceOutline
    {
        internal static IReadOnlyList<IslandSurfacePoint> Create(double width, double height,
            double shoulder, double lowerRadius)
        {
            shoulder = Math.Clamp(shoulder, 0, Math.Min(width / 4, height / 2));
            double radius = Math.Clamp(lowerRadius, 0, Math.Min((width - shoulder * 2) / 2, height - shoulder));
            var points = new List<IslandSurfacePoint> { new(0, 0), new(width, 0) };
            Curve(points, new(width, 0), new(width - shoulder, 0), new(width - shoulder, shoulder));
            points.Add(new(width - shoulder, height - radius));
            Curve(points, new(width - shoulder, height - radius), new(width - shoulder, height),
                new(width - shoulder - radius, height));
            points.Add(new(shoulder + radius, height));
            Curve(points, new(shoulder + radius, height), new(shoulder, height), new(shoulder, height - radius));
            points.Add(new(shoulder, shoulder));
            Curve(points, new(shoulder, shoulder), new(shoulder, 0), new(0, 0));
            return points;
        }

        private static void Curve(List<IslandSurfacePoint> points, IslandSurfacePoint from,
            IslandSurfacePoint control, IslandSurfacePoint to)
        {
            // Sixteen segments per curve are subpixel at this prototype's tested DPI range.
            for (int step = 1; step <= 16; step++)
            {
                double t = step / 16.0, u = 1 - t;
                points.Add(new(u * u * from.X + 2 * u * t * control.X + t * t * to.X,
                    u * u * from.Y + 2 * u * t * control.Y + t * t * to.Y));
            }
        }
    }
}
