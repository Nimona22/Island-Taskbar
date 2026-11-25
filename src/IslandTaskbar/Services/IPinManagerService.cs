using System;
using System.Threading;
using System.Threading.Tasks;
using IslandTaskbar.Models;

namespace IslandTaskbar.Services
{
    /// <summary>
    /// Owns its store/resolver; borrows the existing window service. Serializes save-before-publish
    /// mutations and runtime-only enrichment. Events may arrive off the UI thread (D-014).
    /// </summary>
    internal interface IPinManagerService : IAsyncDisposable
    {
        /// <summary>Signals that a new immutable Current snapshot is available.</summary>
        event EventHandler? Changed;

        /// <summary>The latest committed pins and independent runtime enrichment.</summary>
        PinManagerSnapshot Current { get; }

        /// <summary>Loads/reloads explicitly, attempting writer recovery without discarding committed data on failure.</summary>
        Task<PinsLoadResult> InitializeAsync(CancellationToken cancellationToken);

        /// <summary>Resolves and appends a new GUID pin, or returns the existing canonical reference idempotently.</summary>
        Task<PinMutationResult> PinAsync(LaunchReference source, CancellationToken cancellationToken);

        /// <summary>Removes a pin only after saving; never affects a running application or source file.</summary>
        Task<PinMutationResult> UnpinAsync(PinId pinId, CancellationToken cancellationToken);

        /// <summary>Moves before another current pin; null appends. Self/unchanged moves do not write.</summary>
        Task<PinMutationResult> MoveAsync(PinId pinId, PinId? beforePinId, CancellationToken cancellationToken);

        /// <summary>
        /// Resolves a current window for a possible Pin action without persistence. Native work is off-UI
        /// and cancellable. Stale IDs return Missing with no identity/reference before/after enrichment.
        /// </summary>
        Task<ApplicationWindowIdentityResult> ResolveWindowAsync(RunningWindowId runningWindowId,
            CancellationToken cancellationToken);

        /// <summary>Revalidates a current pin without persisting runtime state or changing its position.</summary>
        Task RefreshAvailabilityAsync(PinId pinId, CancellationToken cancellationToken);
    }
}

