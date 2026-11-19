using Microsoft.UI.Xaml;

namespace IslandTaskbar.Presentation.Windows
{
    /// <summary>
    /// Top-level WinUI 3 window shell for IslandTaskbar.
    /// Phase 1 bootstrap contains only minimal initialization; topmost behavior, anchoring,
    /// DPI positioning, and hit testing are deferred to Phase 2 (D-009, OD-010, OD-011).
    /// </summary>
    public sealed partial class IslandWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of <see cref="IslandWindow"/>.
        /// </summary>
        public IslandWindow()
        {
            InitializeComponent();
        }
    }
}
