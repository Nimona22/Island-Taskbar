using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IslandTaskbar.Models;

namespace IslandTaskbar.Adapters.Interfaces
{
    /// <summary>Boundary for Windows media sessions, independent of XAML.</summary>
    internal interface IMediaSessionService : IDisposable
    {
        event EventHandler? SessionsChanged;
        IReadOnlyList<MediaSessionState> Sessions { get; }
        string? CurrentSessionId { get; }
        string? UnavailableReason { get; }
        Task InitializeAsync(CancellationToken cancellationToken);
        Task<MediaCommandResult> ExecuteAsync(string id, MediaCommand command, CancellationToken cancellationToken);
    }
}
