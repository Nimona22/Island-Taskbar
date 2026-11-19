using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using IslandTaskbar.Adapters.Interfaces;

namespace IslandTaskbar.Adapters.Implementations
{
    /// <summary>
    /// Configures the existing WinUI window exclusively through Windows App SDK APIs.
    /// </summary>
    internal sealed class WindowsAppSdkIslandWindowAdapter : IIslandWindowAdapter
    {
        // Borrowed from WinUI: disposal releases this reference, not the underlying window.
        private AppWindow? _appWindow;

        internal WindowsAppSdkIslandWindowAdapter(Window window)
        {
            ArgumentNullException.ThrowIfNull(window);
            _appWindow = window.AppWindow;
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
        }
    }
}
