using System;
using Microsoft.UI.Xaml;

namespace IslandTaskbar.Presentation.Windows
{
    /// <summary>
    /// Top-level WinUI 3 window shell for IslandTaskbar.
    /// Phase 2.2 contains a temporary diagnostic surface; Windows behavior belongs to the adapters.
    /// </summary>
    public sealed partial class IslandWindow : Window
    {
        /// <summary>
        /// Requests that the application close the temporary Phase 2.2 test window.
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
        }

        private void OnTemporaryCloseClick(object sender, RoutedEventArgs e)
        {
            TemporaryCloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
