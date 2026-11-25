using System;
using System.Threading;
using System.Threading.Tasks;
using IslandTaskbar.Models;

namespace IslandTaskbar.Persistence
{
    /// <summary>Owns the writer lease and atomic, revision-checked v1 persistence (D-014).</summary>
    internal interface IPinsFileStore : IAsyncDisposable
    {
        /// <summary>Loads without overwriting; attempts/retries the lease and reloads before enabling writes.</summary>
        Task<PinsLoadResult> LoadAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Saves only under its lease with the expected revision; null means the file was missing.
        /// Cancellation may throw before commit; a completed commit must return Saved.
        /// </summary>
        Task<PinsSaveResult> SaveAsync(PinsData data, string? expectedRevision,
            CancellationToken cancellationToken);
    }
}

