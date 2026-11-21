using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using BackdropCompositor = Windows.UI.Composition.Compositor;
using BackdropBrush = Windows.UI.Composition.CompositionBrush;

namespace IslandTaskbar.Presentation.Backdrops
{
    /// <summary>Unblurred host backdrop under the warm tint; no CPU capture or native style changes.</summary>
    internal sealed class TransparentIslandBackdrop : SystemBackdrop
    {
        private BackdropCompositor? _compositor;
        private BackdropBrush? _brush;

        protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
        {
            // Windows composition needs the OS queue as well as WinUI's queue; WinUI owns its shutdown.
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().EnsureSystemDispatcherQueue();
            _compositor = new BackdropCompositor();
            _brush = _compositor.CreateHostBackdropBrush();
            target.SystemBackdrop = _brush;
        }

        protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
        {
            target.SystemBackdrop = null;
            _brush?.Dispose();
            _brush = null;
            _compositor?.Dispose();
            _compositor = null;
        }
    }
}
