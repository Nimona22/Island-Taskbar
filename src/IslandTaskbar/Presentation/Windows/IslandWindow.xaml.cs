using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using IslandTaskbar.Models;

namespace IslandTaskbar.Presentation.Windows
{
    /// <summary>
    /// Top-level WinUI 3 window shell for IslandTaskbar.
    /// Phase 2.3 contains a temporary shape/input surface; native window behavior belongs to the adapters.
    /// </summary>
    public sealed partial class IslandWindow : Window
    {
        private int _islandClickCount;

        internal event EventHandler<IslandSurfaceLayoutEventArgs>? SurfaceLayoutChanged;

        /// <summary>
        /// Requests that the application close the temporary test window.
        /// </summary>
        public event EventHandler? TemporaryCloseRequested;

        /// <summary>
        /// Initializes a new instance of <see cref="IslandWindow"/>.
        /// </summary>
        public IslandWindow()
        {
            InitializeComponent();
            // WinUI cannot construct Thickness as a XAML object element; preserve the Space.Section token.
            DiagnosticSurface.Padding = new Thickness((double)Application.Current.Resources["Space.Section"]);
            double exteriorMargin = (double)Application.Current.Resources["Space.Major"];
            IslandSurface.Margin = new Thickness(exteriorMargin, 0, exteriorMargin, exteriorMargin);
        }

        private void OnSurfaceLoaded(object sender, RoutedEventArgs e)
        {
            ReportSurfaceLayout();
        }

        private void OnSurfaceSizeChanged(object sender, SizeChangedEventArgs e)
        {
            ReportSurfaceLayout();
        }

        private void ReportSurfaceLayout()
        {
            if (IslandSurface.XamlRoot is null || IslandSurface.ActualWidth <= 0 || IslandSurface.ActualHeight <= 0)
            {
                return;
            }

            CornerRadius corners = IslandSurface.CornerRadius;
            if (corners.TopLeft != 0 || corners.TopRight != 0 || corners.BottomLeft != corners.BottomRight)
            {
                throw new InvalidOperationException("The prototype requires square top corners and equal rounded lower corners.");
            }

            Point position = IslandSurface.TransformToVisual(WindowRoot).TransformPoint(new Point(0, 0));
            SurfaceLayoutChanged?.Invoke(this, new IslandSurfaceLayoutEventArgs(position.X, position.Y,
                IslandSurface.ActualWidth, IslandSurface.ActualHeight, corners.BottomLeft,
                IslandSurface.XamlRoot.RasterizationScale));
        }

        private void OnIslandTapped(object sender, TappedRoutedEventArgs e)
        {
            _islandClickCount++;
            ClickFeedback.Text = $"Island clicks: {_islandClickCount}";
        }

        private void OnTemporaryCloseClick(object sender, RoutedEventArgs e)
        {
            TemporaryCloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
