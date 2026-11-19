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

        public void SetSurfaceRegion(int left, int top, int width, int height, int lowerCornerRadius)
        {
            _ = AppWindow;
            if (width <= 0 || height <= 0 || lowerCornerRadius < 0)
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
            HRGN region = default;
            HRGN squareTop = default;

            try
            {
                region = radius == 0
                    ? PInvoke.CreateRectRgn(x, y, right, bottom)
                    : PInvoke.CreateRoundRectRgn(x, y, right, bottom, radius * 2, radius * 2);
                if (region == default)
                {
                    throw NativeFailure("Create island region");
                }

                if (radius > 0)
                {
                    // Radius.Island has square upper corners and matching rounded lower corners.
                    squareTop = PInvoke.CreateRectRgn(x, y, right, bottom - radius);
                    if (squareTop == default)
                    {
                        throw NativeFailure("Create square upper region");
                    }

                    if ((int)PInvoke.CombineRgn(region, region, squareTop, RGN_COMBINE_MODE.RGN_OR) == 0)
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
                DeleteOwnedRegion(squareTop);
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
