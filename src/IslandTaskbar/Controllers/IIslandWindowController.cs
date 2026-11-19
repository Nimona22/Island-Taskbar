using System;

namespace IslandTaskbar.Controllers
{
    /// <summary>
    /// Coordinates initialization and lifetime of the Phase 2.2 island window.
    /// </summary>
    internal interface IIslandWindowController : IDisposable
    {
        /// <summary>
        /// Configures and shows the window at the primary display's top-center.
        /// </summary>
        void Initialize();
    }
}
