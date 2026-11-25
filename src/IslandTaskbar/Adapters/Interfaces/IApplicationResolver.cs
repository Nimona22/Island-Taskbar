using System;
using System.Threading;
using System.Threading.Tasks;
using IslandTaskbar.Models;

namespace IslandTaskbar.Adapters.Interfaces
{
    /// <summary>Owns native resolution and its apartment resources; never launches or enumerates windows (D-014).</summary>
    internal interface IApplicationResolver : IAsyncDisposable
    {
        /// <summary>Validates a typed source, resolves real metadata and preserves its launch intent.</summary>
        Task<ApplicationResolutionResult> ResolveAsync(LaunchReference source, CancellationToken cancellationToken);

        /// <summary>Identifies only the supplied live window; inability to enrich never removes a window entry.</summary>
        Task<ApplicationWindowIdentityResult> IdentifyWindowAsync(RunningWindowId runningWindowId,
            CancellationToken cancellationToken);
    }
}

