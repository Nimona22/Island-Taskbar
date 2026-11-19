using System;
using Microsoft.UI.Windowing;
using IslandTaskbar.Adapters.Interfaces;

namespace IslandTaskbar.Adapters.Implementations
{
    /// <summary>
    /// Reads the primary monitor through the Windows App SDK without display-change listeners.
    /// </summary>
    internal sealed class WindowsAppSdkDisplayService : IDisplayService
    {
        public (int X, int Y, int Width, int Height) GetPrimaryDisplayBounds()
        {
            var display = DisplayArea.Primary
                ?? throw new InvalidOperationException("The primary display is unavailable.");
            var bounds = display.OuterBounds;
            return (bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }
    }
}
