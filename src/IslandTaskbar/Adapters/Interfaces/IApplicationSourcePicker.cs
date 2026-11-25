using System;
using System.Threading;
using System.Threading.Tasks;
using IslandTaskbar.Models;

namespace IslandTaskbar.Adapters.Interfaces
{
    /// <summary>Owns an IslandWindow-parented native chooser and its apartment resources (D-014).</summary>
    internal interface IApplicationSourcePicker : IAsyncDisposable
    {
        /// <summary>Selects a local EXE/LNK source or returns explicit cancellation/unavailability/failure.</summary>
        Task<ApplicationSourceSelectionResult> PickAsync(CancellationToken cancellationToken);
    }
}

