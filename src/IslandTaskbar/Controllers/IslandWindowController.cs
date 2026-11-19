using System;
using IslandTaskbar.Adapters.Interfaces;

namespace IslandTaskbar.Controllers
{
    /// <summary>
    /// Coordinates the island window through narrow window and display adapters (D-009).
    /// </summary>
    internal sealed class IslandWindowController : IIslandWindowController
    {
        // PROVISIONAL — Phase 2.2 diagnostic dimensions, not final product dimensions (OD-019).
        // AppWindow bounds use physical pixels. DPI-aware sizing belongs to a later milestone.
        private const int ProvisionalWindowWidthPixels = 640;
        private const int ProvisionalWindowHeightPixels = 240;

        private readonly IIslandWindowAdapter _windowAdapter;
        private readonly IDisplayService _displayService;
        private bool _isInitialized;
        private bool _isDisposed;

        internal IslandWindowController(IIslandWindowAdapter windowAdapter, IDisplayService displayService)
        {
            ArgumentNullException.ThrowIfNull(windowAdapter);
            ArgumentNullException.ThrowIfNull(displayService);
            _windowAdapter = windowAdapter;
            _displayService = displayService;
        }

        public void Initialize()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_isInitialized)
            {
                throw new InvalidOperationException("The island window has already been initialized.");
            }

            var displayBounds = _displayService.GetPrimaryDisplayBounds();
            if (displayBounds.Width <= 0 || displayBounds.Height <= 0)
            {
                throw new InvalidOperationException("The primary display has no usable screen bounds.");
            }

            int width = Math.Min(ProvisionalWindowWidthPixels, displayBounds.Width);
            int height = Math.Min(ProvisionalWindowHeightPixels, displayBounds.Height);
            // Integer pixel placement rounds a half-pixel center toward the display's left edge.
            int x = displayBounds.X + (displayBounds.Width - width) / 2;
            int y = displayBounds.Y;

            _windowAdapter.ConfigureOverlay();
            _windowAdapter.MoveAndResize(x, y, width, height);
            _windowAdapter.ShowWithoutActivation();
            _isInitialized = true;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _windowAdapter.Dispose();
        }
    }
}
