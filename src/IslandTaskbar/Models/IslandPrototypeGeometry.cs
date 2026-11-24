namespace IslandTaskbar.Models
{
    /// <summary>
    /// PROVISIONAL Phase 3.2 dimensions in DIPs; the Phase 3.1 idle notch is preserved.
    /// </summary>
    internal static class IslandPrototypeGeometry
    {
        internal const double NotchWidth = 126;
        internal const double NotchHeight = 34;
        internal const double ExpandedWidth = 320;
        internal const double ExpandedHeight = 68;
        internal const double ExpandedTop = 0;
        internal const double ShoulderInset = 12;
        internal const double TaskbarWidth = 520;
        internal const double TaskbarHeight = 84;
        // Phase 5.1 host accommodates Taskbar Mode; native regions still exclude all margins.
        internal const double HostWidth = 560;
        internal const double HostHeight = 96;
    }
}
