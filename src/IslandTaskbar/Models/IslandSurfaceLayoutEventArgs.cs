using System;
using System.Collections.Generic;

namespace IslandTaskbar.Models
{
    /// <summary>
    /// Describes the visible prototype surface in WinUI layout units, without native handles.
    /// </summary>
    internal sealed class IslandSurfaceLayoutEventArgs : EventArgs
    {
        internal IslandSurfaceLayoutEventArgs(double left, double top, double width, double height,
            double lowerCornerRadius, double rasterizationScale, double upperCornerRadius = 0,
            IReadOnlyList<IslandSurfacePoint>? outline = null)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
            LowerCornerRadius = lowerCornerRadius;
            UpperCornerRadius = upperCornerRadius;
            RasterizationScale = rasterizationScale;
            Outline = outline;
        }

        internal double Left { get; }
        internal double Top { get; }
        internal double Width { get; }
        internal double Height { get; }
        internal double LowerCornerRadius { get; }
        internal double UpperCornerRadius { get; }
        internal double RasterizationScale { get; }
        internal IReadOnlyList<IslandSurfacePoint>? Outline { get; }
    }
}
