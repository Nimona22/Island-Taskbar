using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace IslandTaskbar.Presentation.Backdrops
{
    using Windows = global::Windows;

    internal sealed class TransparentIslandBackdrop : SystemBackdrop
    {
        private DesktopAcrylicController? _acrylicController;
        private SystemBackdropConfiguration? _configurationSource;

        protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
        {
            base.OnTargetConnected(target, root);

            if (!DesktopAcrylicController.IsSupported())
            {
                return;
            }

            _configurationSource = new SystemBackdropConfiguration
            {
                IsInputActive = true,
                Theme = SystemBackdropTheme.Dark
            };

            _acrylicController = new DesktopAcrylicController
            {
                TintColor = Windows.UI.Color.FromArgb(255, 18, 18, 22),
                TintOpacity = 0.12f,
                LuminosityOpacity = 0.15f
            };

            _acrylicController.AddSystemBackdropTarget(target);
            _acrylicController.SetSystemBackdropConfiguration(_configurationSource);
        }

        protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
        {
            base.OnTargetDisconnected(target);
            _acrylicController?.RemoveSystemBackdropTarget(target);
            _acrylicController?.Dispose();
            _acrylicController = null;
            _configurationSource = null;
        }
    }
}
