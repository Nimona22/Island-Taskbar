using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using IslandTaskbar.Adapters.Interfaces;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace IslandTaskbar.Adapters.Implementations
{
    /// <summary>
    /// Uses Windows App SDK windowing, with CsWin32 bindings only for the prototype's native shape.
    /// </summary>
    internal sealed unsafe class WindowsAppSdkIslandWindowAdapter : IIslandWindowAdapter
    {
        // Borrowed from WinUI: disposal releases this reference, not the underlying window.
        private AppWindow? _appWindow;
        private nint _windowHandle;

        internal WindowsAppSdkIslandWindowAdapter(Window window)
        {
            ArgumentNullException.ThrowIfNull(window);
            _appWindow = window.AppWindow;
            _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            if (_windowHandle == 0)
            {
                throw new InvalidOperationException("The island window has no native handle.");
            }
        }

        private AppWindow AppWindow => _appWindow
            ?? throw new ObjectDisposedException(nameof(WindowsAppSdkIslandWindowAdapter));

        public void ConfigureOverlay()
        {
            if (AppWindow.Presenter is not OverlappedPresenter presenter)
            {
                throw new InvalidOperationException("The island window requires an overlapped presenter.");
            }

            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;

            // Keep the native background and input excluded until WinUI reports a real surface layout.
            HRGN emptyRegion = PInvoke.CreateRectRgn(0, 0, 0, 0);
            if (emptyRegion == default)
            {
                throw NativeFailure("CreateRectRgn");
            }

            try
            {
                if (PInvoke.SetWindowRgn((HWND)_windowHandle, emptyRegion, false) == 0)
                {
                    throw NativeFailure("SetWindowRgn");
                }

                // SetWindowRgn transfers ownership to Windows; never delete a transferred region.
                emptyRegion = default;
            }
            finally
            {
                DeleteOwnedRegion(emptyRegion);
            }
        }

        public void MoveAndResize(int x, int y, int width, int height)
        {
            AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
            // Phase 3.1 runtime measured a retained client inset (3 physical pixels at 125%).
            // Anchor the painted client surface to y, using the same offset query as SetSurfaceRegion.
            // This changes placement only; presenter, activation and topmost behavior stay as configured.
            HWND hwnd = (HWND)_windowHandle;
            RECT windowBounds;
            System.Drawing.Point clientOrigin = default;
            if (!PInvoke.GetWindowRect(hwnd, &windowBounds) || !PInvoke.ClientToScreen(hwnd, &clientOrigin))
            {
                throw NativeFailure("Read top-edge client inset");
            }

            int topInset = checked(clientOrigin.Y - windowBounds.top);
            if (topInset != 0)
            {
                AppWindow.Move(new PointInt32(x, checked(y - topInset)));
            }
        }

        public void ShowWithoutActivation()
        {
            AppWindow.Show(false);
        }

        public void Dispose()
        {
            _appWindow = null;
            _windowHandle = 0;
        }

        public void SetSurfaceRegion(int left, int top, int width, int height, int lowerCornerRadius, int upperCornerRadius)
        {
            _ = AppWindow;
            if (width <= 0 || height <= 0 || lowerCornerRadius < 0 || upperCornerRadius < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "The surface must have positive dimensions.");
            }

            HWND hwnd = (HWND)_windowHandle;
            RECT windowBounds;
            System.Drawing.Point clientOrigin = default;
            if (!PInvoke.GetWindowRect(hwnd, &windowBounds) || !PInvoke.ClientToScreen(hwnd, &clientOrigin))
            {
                throw NativeFailure("Read client-to-window offset");
            }

            // SetWindowRgn uses window-relative coordinates; WinUI supplies client-relative geometry.
            int x = checked(left + clientOrigin.X - windowBounds.left);
            int y = checked(top + clientOrigin.Y - windowBounds.top);
            int right = checked(x + width);
            int bottom = checked(y + height);
            int radius = Math.Min(lowerCornerRadius, Math.Min(width, height) / 2);
            int upperRadius = Math.Min(upperCornerRadius, Math.Min(width, height) / 2);
            HRGN region = default;
            HRGN upperRegion = default;
            HRGN upperHalf = default;
            HRGN lowerHalf = default;

            try
            {
                region = radius == 0
                    ? PInvoke.CreateRectRgn(x, y, right, bottom)
                    // GDI's rounded-region endpoint removes a pixel; preserve the measured XAML extent.
                    : PInvoke.CreateRoundRectRgn(x, y, checked(right + 1), checked(bottom + 1), radius * 2, radius * 2);
                if (region == default)
                {
                    throw NativeFailure("Create island region");
                }

                if (upperRadius != radius)
                {
                    // Keep the region approach: union independently rounded upper/lower halves.
                    int middle = y + height / 2;
                    upperRegion = upperRadius == 0
                        ? PInvoke.CreateRectRgn(x, y, right, bottom)
                        : PInvoke.CreateRoundRectRgn(x, y, checked(right + 1), checked(bottom + 1), upperRadius * 2, upperRadius * 2);
                    upperHalf = PInvoke.CreateRectRgn(x, y, right, middle);
                    lowerHalf = PInvoke.CreateRectRgn(x, middle, right, bottom);
                    if (upperRegion == default || upperHalf == default || lowerHalf == default)
                    {
                        throw NativeFailure("Create paired corner regions");
                    }

                    if ((int)PInvoke.CombineRgn(upperRegion, upperRegion, upperHalf, RGN_COMBINE_MODE.RGN_AND) == 0
                        || (int)PInvoke.CombineRgn(region, region, lowerHalf, RGN_COMBINE_MODE.RGN_AND) == 0
                        || (int)PInvoke.CombineRgn(region, region, upperRegion, RGN_COMBINE_MODE.RGN_OR) == 0)
                    {
                        throw NativeFailure("Combine island region");
                    }
                }

                if (PInvoke.SetWindowRgn(hwnd, region, true) == 0)
                {
                    throw NativeFailure("SetWindowRgn");
                }

                region = default;
            }
            finally
            {
                DeleteOwnedRegion(upperRegion);
                DeleteOwnedRegion(upperHalf);
                DeleteOwnedRegion(lowerHalf);
                DeleteOwnedRegion(region);
            }
        }

        public void SetSurfacePolygon(int[] coordinates)
        {
            _ = AppWindow;
            ArgumentNullException.ThrowIfNull(coordinates);
            if (coordinates.Length < 6 || coordinates.Length % 2 != 0)
            {
                throw new ArgumentException("An outline requires complete coordinate pairs.", nameof(coordinates));
            }

            HWND hwnd = (HWND)_windowHandle;
            RECT bounds;
            System.Drawing.Point origin = default;
            if (!PInvoke.GetWindowRect(hwnd, &bounds) || !PInvoke.ClientToScreen(hwnd, &origin))
            {
                throw NativeFailure("Read polygon client offset");
            }

            var points = new System.Drawing.Point[coordinates.Length / 2];
            for (int index = 0; index < points.Length; index++)
            {
                points[index] = new(checked(coordinates[index * 2] + origin.X - bounds.left),
                    checked(coordinates[index * 2 + 1] + origin.Y - bounds.top));
            }

            HRGN region = default;
            try
            {
                fixed (System.Drawing.Point* first = points)
                {
                    region = PInvoke.CreatePolygonRgn(first, points.Length, CREATE_POLYGON_RGN_MODE.WINDING);
                }

                if (region == default) throw NativeFailure("Create notch polygon");
                if (PInvoke.SetWindowRgn(hwnd, region, true) == 0) throw NativeFailure("Set notch polygon");
                region = default;
            }
            finally
            {
                DeleteOwnedRegion(region);
            }
        }

        private static Win32Exception NativeFailure(string operation)
        {
            return new Win32Exception(Marshal.GetLastWin32Error(), $"{operation} failed.");
        }

        private static void DeleteOwnedRegion(HRGN region)
        {
            if (region != default && !PInvoke.DeleteObject((HGDIOBJ)region))
            {
                // Do not mask an earlier failure while unwinding a native allocation.
                Debug.WriteLine($"DeleteObject failed: {Marshal.GetLastWin32Error()}");
            }
        }
    }
}
