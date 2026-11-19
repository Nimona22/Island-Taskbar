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
    }
}
