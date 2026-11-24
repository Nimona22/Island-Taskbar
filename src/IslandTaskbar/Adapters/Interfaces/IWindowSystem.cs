using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IslandTaskbar.Models;

namespace IslandTaskbar.Adapters.Interfaces
{
    internal interface IWindowSystem : IDisposable
    {
        event EventHandler? WindowsChanged;
        void Initialize();
        Task<IReadOnlyList<RunningWindow>> EnumerateAsync(CancellationToken cancellationToken);
        Task<WindowActivationResult> ActivateAsync(RunningWindow window, CancellationToken cancellationToken);
    }
}
