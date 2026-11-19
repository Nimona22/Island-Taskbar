using System;
using IslandTaskbar.Adapters.Interfaces;
using IslandTaskbar.Models;

namespace IslandTaskbar.Controllers
{
    /// <summary>
    /// Coordinates the island window through narrow window and display adapters (D-009).
    /// </summary>
    internal sealed class IslandWindowController : IIslandWindowController
    {
        // PROVISIONAL — Phase 2.3 diagnostic bounds, not final product dimensions (OD-019).
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

        public void UpdateSurfaceRegion(IslandSurfaceLayoutEventArgs layout)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            ArgumentNullException.ThrowIfNull(layout);
            if (!double.IsFinite(layout.RasterizationScale) || layout.RasterizationScale <= 0
                || !double.IsFinite(layout.Left) || !double.IsFinite(layout.Top)
                || !double.IsFinite(layout.Width) || !double.IsFinite(layout.Height)
                || !double.IsFinite(layout.LowerCornerRadius) || layout.LowerCornerRadius < 0
                || layout.Width <= 0 || layout.Height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(layout), "The surface layout must be finite and positive.");
            }

            // Convert the actual WinUI geometry once at the adapter boundary, not with a second UI scale.
            double scale = layout.RasterizationScale;
            int left = checked((int)Math.Round(layout.Left * scale));
            int top = checked((int)Math.Round(layout.Top * scale));
            int right = checked((int)Math.Round((layout.Left + layout.Width) * scale));
            int bottom = checked((int)Math.Round((layout.Top + layout.Height) * scale));
            int radius = checked((int)Math.Round(layout.LowerCornerRadius * scale));
            if (right <= left || bottom <= top)
            {
                throw new ArgumentOutOfRangeException(nameof(layout), "The surface must occupy physical pixels.");
            }

            _windowAdapter.SetSurfaceRegion(left, top, right - left, bottom - top, radius);
        }
    }
}
