using System;
using System.Threading;
using System.Threading.Tasks;
using IslandTaskbar.Models;

namespace IslandTaskbar.Services
{
    /// <summary>Owns launcher/picker; borrows pins/resolver. Pin clicks are launch-only (D-014).</summary>
    internal interface IApplicationLauncherService : IAsyncDisposable
    {
        /// <summary>Snapshots and revalidates a pin, rejecting stale targets and duplicate in-flight dispatch.</summary>
        Task<ApplicationLaunchResult> OpenAsync(PinId pinId, CancellationToken cancellationToken);

        /// <summary>Selects only; the caller explicitly passes a selected source to PinAsync.</summary>
        Task<ApplicationSourceSelectionResult> SelectSourceAsync(CancellationToken cancellationToken);
    }
}

