using System;
using System.Collections.Generic;

namespace IslandTaskbar.Models
{
    internal static class TaskbarRowSizing
    {
        internal static (double Pins, double Windows) GetViewportHeights(
            double pins, double windows, double pinRow, double windowRow, double viewportBudget)
        {
            if (pins + windows <= viewportBudget) return (pins, windows);

            double pinMinimum = Math.Min(pinRow, pins);
            double windowMinimum = Math.Min(windowRow, windows);
            // D-013: when minimum rows exceed the budget, retain them for the outer scroller.
            if (pinMinimum + windowMinimum > viewportBudget) return (pinMinimum, windowMinimum);

            return (pinMinimum, Math.Min(windows, viewportBudget - pinMinimum));
        }

        internal static double GetRowHeight(IEnumerable<double> measuredItemHeights, double minimumHeight)
        {
            ArgumentNullException.ThrowIfNull(measuredItemHeights);
            if (!double.IsFinite(minimumHeight) || minimumHeight < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumHeight));
            }

            double rowHeight = minimumHeight;
            foreach (double measuredHeight in measuredItemHeights)
            {
                if (!double.IsFinite(measuredHeight) || measuredHeight < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(measuredItemHeights));
                }

                rowHeight = Math.Max(rowHeight, measuredHeight);
            }

            return rowHeight;
        }
    }
}
