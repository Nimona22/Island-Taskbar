using System;
using System.Threading;
using System.Threading.Tasks;
using IslandTaskbar.Models;

namespace IslandTaskbar.Adapters.Interfaces
{
    /// <summary>Native dispatch only; never activates a selected running window or elevates automatically (D-014).</summary>
    internal interface IApplicationLauncher : IAsyncDisposable
    {
        /// <summary>
        /// Launches a revalidated target. After possible dispatch, return Accepted or OutcomeUnknown
        /// despite cancellation; never report Canceled/throw cancellation as if dispatch was undone.
        /// </summary>
        Task<ApplicationLaunchResult> LaunchAsync(LaunchReference launchReference,
            CancellationToken cancellationToken);
    }
}

