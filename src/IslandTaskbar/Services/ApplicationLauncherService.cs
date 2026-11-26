using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IslandTaskbar.Adapters.Interfaces;
using IslandTaskbar.Models;

namespace IslandTaskbar.Services
{
    /// <summary>Launch-only pin orchestration. Owns picker/launcher; borrows pins (D-014/D-015).</summary>
    internal sealed class ApplicationLauncherService : IApplicationLauncherService
    {
        private readonly IPinManagerService _pins;
        private readonly IApplicationLauncher _launcher;
        private readonly IApplicationSourcePicker _picker;
        private readonly object _gate = new();
        private readonly HashSet<PinId> _opening = new();
        private readonly CancellationTokenSource _lifetime = new();
        private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _operations;
        private bool _disposing;
        private Task? _disposeTask;

        internal ApplicationLauncherService(IPinManagerService pins, IApplicationLauncher launcher,
            IApplicationSourcePicker picker)
        {
            _pins = pins;
            _launcher = launcher;
            _picker = picker;
        }

        public Task<ApplicationLaunchResult> OpenAsync(PinId pinId, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposing, this);
                if (!_opening.Add(pinId)) return Task.FromResult(new ApplicationLaunchResult(ApplicationLaunchStatus.Busy));
                _operations++;
            }
            return RunAsync(async token =>
            {
                var pin = _pins.Current.Pins.FirstOrDefault(item => item.Id == pinId);
                if (pin is null) return Stale();
                var reference = pin.LaunchReference;
                await _pins.RefreshAvailabilityAsync(pinId, token).ConfigureAwait(false);
                var snapshot = _pins.Current;
                pin = snapshot.Pins.FirstOrDefault(item => item.Id == pinId);
                if (pin is null || pin.LaunchReference != reference) return Stale();
                if (!snapshot.RuntimeStates.TryGetValue(pinId, out var runtime) ||
                    runtime.Resolution?.Status != ApplicationResolutionStatus.Available)
                    return new ApplicationLaunchResult(ApplicationLaunchStatus.Unavailable,
                        runtime?.Resolution?.Message ?? "Check the application target, then retry availability.");
                token.ThrowIfCancellationRequested();
                // The native adapter revalidates immediately before dispatch. There is no activation path here.
                return await _launcher.LaunchAsync(reference, token).ConfigureAwait(false);
            }, cancellationToken, pinId);
        }

        public Task<ApplicationSourceSelectionResult> SelectSourceAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposing, this);
                _operations++;
            }
            // RunAsync invokes the picker on the caller thread so its deferred owner is captured correctly.
            return RunAsync(token => _picker.PickAsync(token), cancellationToken, null);
        }

        private static ApplicationLaunchResult Stale() => new(ApplicationLaunchStatus.StaleTarget,
            "This pin changed or was removed. Select the current pinned entry.");

        private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken, PinId? pin)
        {
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
                linked.Token.ThrowIfCancellationRequested();
                return await action(linked.Token).ConfigureAwait(false);
            }
            finally
            {
                lock (_gate)
                {
                    if (pin is { } id) _opening.Remove(id);
                    if (--_operations == 0 && _disposing) _drained.TrySetResult();
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (_gate)
            {
                if (_disposeTask is not null) return new(_disposeTask);
                _disposing = true;
                if (_operations == 0) _drained.TrySetResult();
                _disposeTask = DisposeCoreAsync();
                return new(_disposeTask);
            }
        }

        private async Task DisposeCoreAsync()
        {
            _lifetime.Cancel();
            await _drained.Task.ConfigureAwait(false);
            try { await _picker.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                try { await _launcher.DisposeAsync().ConfigureAwait(false); }
                finally { _lifetime.Dispose(); }
            }
        }
    }
}
