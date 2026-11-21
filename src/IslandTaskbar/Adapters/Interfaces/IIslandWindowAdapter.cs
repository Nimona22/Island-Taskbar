using System;

namespace IslandTaskbar.Adapters.Interfaces
{
    /// <summary>
    /// Applies Windows-specific behavior to the application's own island window.
    /// </summary>
    internal interface IIslandWindowAdapter : IDisposable
    {
        /// <summary>
        /// Removes the frame and configures a fixed, topmost overlay.
        /// </summary>
        void ConfigureOverlay();

        /// <summary>
        /// Sets screen position and size in physical pixels.
        /// </summary>
        void MoveAndResize(int x, int y, int width, int height);

        /// <summary>
        /// Shows the window without requesting activation.
        /// </summary>
        void ShowWithoutActivation();

        /// <summary>
        /// Sets the visible and input outline in client-relative physical pixels, with paired corner radii.
        /// </summary>
        void SetSurfaceRegion(int left, int top, int width, int height, int lowerCornerRadius, int upperCornerRadius);
        /// <summary>Sets a sampled outline in client-relative physical-pixel coordinate pairs.</summary>
        void SetSurfacePolygon(int[] coordinates);
    }
}
