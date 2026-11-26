using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using IslandTaskbar.Models;
using IslandTaskbar.Services;

namespace IslandTaskbar.ViewModels
{
    internal sealed class TaskbarViewModel : INotifyPropertyChanged, IDisposable, IAsyncDisposable
    {
        private readonly WindowManagerService _windows;
        private readonly IPinManagerService _pins;
        private readonly IApplicationLauncherService _applications;
        private readonly DispatcherQueue _dispatcher;
        private readonly CancellationTokenSource _lifetime = new();
        private bool _busy;
        private bool _pickerActive;
        private bool _disposed;
        private string? _commandStatus;
        private TaskCompletionSource? _commandDrained;
        private Task? _initialization;
        private Task? _disposal;
        private int _launches;
        private TaskCompletionSource? _launchesDrained;

        internal TaskbarViewModel(WindowManagerService windows, IPinManagerService pins,
            IApplicationLauncherService applications, DispatcherQueue dispatcher)
        {
            _windows = windows;
            _pins = pins;
            _applications = applications;
            _dispatcher = dispatcher;
            _windows.Changed += OnWindowsChanged;
            _pins.Changed += OnPinsChanged;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public ObservableCollection<RunningWindowItemViewModel> Windows { get; } = new();
        public ObservableCollection<PinnedAppItemViewModel> Pins { get; } = new();
        public string StatusText => _busy ? "Working…" : _launches > 0 ? "Opening application…" : _commandStatus ?? _pins.Current.Message ?? _windows.UnavailableReason ??
            (Windows.Count == 0 ? "No open windows" : "");
        public Visibility StatusVisibility => string.IsNullOrEmpty(StatusText) ? Visibility.Collapsed : Visibility.Visible;
        public bool PickerActive => _pickerActive;

        public Task InitializeAsync(CancellationToken cancellationToken) => _initialization = InitializeCoreAsync(cancellationToken);

        private async Task InitializeCoreAsync(CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            await Task.WhenAll(_windows.InitializeAsync(linked.Token), _pins.InitializeAsync(linked.Token));
            if (!_disposed) { ReconcileWindows(); ReconcilePins(); }
        }

        private void OnWindowsChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(() =>
        {
            if (!_disposed) ReconcileWindows();
        });

        private void OnPinsChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(() =>
        {
            if (!_disposed) ReconcilePins();
        });

        private void ReconcileWindows()
        {
            var latest = _windows.Current;
            var live = latest.Select(window => window.Id).ToHashSet();
            for (int index = Windows.Count - 1; index >= 0; index--)
                if (!live.Contains(Windows[index].Id)) Windows.RemoveAt(index);
            for (int index = 0; index < latest.Count; index++)
            {
                var window = latest[index];
                var existing = Windows.FirstOrDefault(item => item.Id == window.Id);
                if (existing is null) Windows.Insert(index, new RunningWindowItemViewModel(window));
                else
                {
                    int previous = Windows.IndexOf(existing);
                    if (previous != index) Windows.Move(previous, index);
                    existing.Update(window);
                }
            }
            NotifyState();
        }

        private void ReconcilePins()
        {
            var snapshot = _pins.Current;
            var ids = snapshot.Pins.Select(pin => pin.Id).ToHashSet();
            for (int index = Pins.Count - 1; index >= 0; index--)
                if (!ids.Contains(Pins[index].Id)) Pins.RemoveAt(index);
            for (int index = 0; index < snapshot.Pins.Length; index++)
            {
                var pin = snapshot.Pins[index];
                snapshot.RuntimeStates.TryGetValue(pin.Id, out var runtime);
                var existing = Pins.FirstOrDefault(item => item.Id == pin.Id);
                if (existing is null) Pins.Insert(index, new PinnedAppItemViewModel(pin, runtime));
                else
                {
                    int previous = Pins.IndexOf(existing);
                    if (previous != index) Pins.Move(previous, index);
                    existing.Update(pin, runtime);
                }
            }
            NotifyState();
        }

        public async Task<bool> ActivateAsync(RunningWindowItemViewModel entry, CancellationToken cancellationToken)
        {
            if (_disposed || _busy) return false;
            BeginCommand();
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
                var result = await _windows.ActivateAsync(entry.Window, linked.Token);
                if (_disposed) return false;
                _commandStatus = result switch
                {
                    WindowActivationResult.Unavailable => "That window is no longer available.",
                    WindowActivationResult.Denied => "Windows did not allow switching. Select the window again.",
                    WindowActivationResult.Failed => "The window could not be restored.",
                    _ => null
                };
                return result == WindowActivationResult.Success;
            }
            catch (OperationCanceledException) when (_disposed || cancellationToken.IsCancellationRequested) { return false; }
            finally { EndCommand(); }
        }

        public async Task LaunchAsync(PinnedAppItemViewModel pin)
        {
            if (_disposed) return;
            if (_launches++ == 0) _launchesDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            NotifyState();
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                var result = await _applications.OpenAsync(pin.Id, linked.Token);
                _commandStatus = result.Status == ApplicationLaunchStatus.Accepted ? "Windows accepted the launch request." :
                result.Message ?? result.Status switch
                {
                    ApplicationLaunchStatus.Busy => "A launch request is already in progress.",
                    ApplicationLaunchStatus.OutcomeUnknown => "The launch outcome is unknown. Check the application before trying again.",
                    ApplicationLaunchStatus.Unavailable or ApplicationLaunchStatus.StaleTarget => "This application is unavailable. Check its target, then retry availability.",
                    _ => "Windows could not start this application. You can retry the pinned entry."
                };
            }
            catch (OperationCanceledException) when (_disposed) { }
            finally
            {
                if (--_launches == 0) _launchesDrained?.TrySetResult();
                if (!_disposed) NotifyState();
            }
        }

        public Task AddApplicationAsync() => RunCommandAsync(async token =>
        {
            _pickerActive = true; NotifyProperty(nameof(PickerActive));
            try
            {
                var selection = await _applications.SelectSourceAsync(token);
                if (selection.Status == ApplicationSourceSelectionStatus.Canceled) return;
                if (selection.Status != ApplicationSourceSelectionStatus.Selected || selection.Source is not { } source)
                { _commandStatus = selection.Message ?? "The application chooser could not select a target. Retry Add application."; return; }
                var result = await _pins.PinAsync(source, token);
                _commandStatus = MutationMessage(result, "Application added to Pinned.");
            }
            finally { _pickerActive = false; NotifyProperty(nameof(PickerActive)); }
        });

        public Task UnpinAsync(PinnedAppItemViewModel pin) => RunMutationAsync(token => _pins.UnpinAsync(pin.Id, token), "Application unpinned.");

        public Task MoveAsync(PinnedAppItemViewModel pin, int delta) => RunCommandAsync(async token =>
        {
            var current = _pins.Current.Pins;
            int index = -1;
            for (int candidate = 0; candidate < current.Length; candidate++)
                if (current[candidate].Id == pin.Id) { index = candidate; break; }
            if (index < 0) return;
            int target = Math.Clamp(index + delta, 0, current.Length - 1);
            if (target == index) { _commandStatus = "This application is already at that end of Pinned."; return; }
            PinId? before = target < index ? current[target].Id : target > index && target + 1 < current.Length ? current[target + 1].Id : null;
            var result = await _pins.MoveAsync(pin.Id, before, token);
            _commandStatus = MutationMessage(result, "Pinned order updated.");
        });

        public Task RetryAvailabilityAsync(PinnedAppItemViewModel pin) => RunCommandAsync(async token =>
        {
            await _pins.RefreshAvailabilityAsync(pin.Id, token);
            _commandStatus = _pins.Current.RuntimeStates.TryGetValue(pin.Id, out var state) &&
                state.Resolution?.Status == ApplicationResolutionStatus.Available ? "Application is available." :
                state?.Resolution?.Message ?? "Application remains unavailable. Check its target and try again.";
        });

        public Task PinWindowAsync(RunningWindowItemViewModel window) => RunCommandAsync(async token =>
        {
                var result = await _pins.ResolveWindowAsync(window.Id, token);
                if (result.Status != ApplicationResolutionStatus.Available || result.RelaunchReference is not { } source)
                { _commandStatus = "A safe launch target is not available for this window."; return; }
                _commandStatus = MutationMessage(await _pins.PinAsync(source, token), "Application pinned.");
        });

        internal async Task<bool> CanPinWindowAsync(RunningWindowItemViewModel window)
        {
            if (_disposed) return false;
            try
            {
                var result = await _pins.ResolveWindowAsync(window.Id, _lifetime.Token);
                return !_disposed && result.Status == ApplicationResolutionStatus.Available && result.RelaunchReference is not null;
            }
            catch (OperationCanceledException) when (_disposed) { return false; }
        }

        public Task ReloadPinsAsync() => RunCommandAsync(async token =>
        {
            var result = await _pins.InitializeAsync(token);
            _commandStatus = result.Message ?? (result.WriteAccess == PinsWriteAccess.Writable ? "Pins reloaded." : "Pins remain read-only. Repair the storage issue, then reload.");
        });

        private async Task RunMutationAsync(Func<CancellationToken, Task<PinMutationResult>> mutation, string success) =>
            await RunCommandAsync(async token => _commandStatus = MutationMessage(await mutation(token), success));

        private async Task RunCommandAsync(Func<CancellationToken, Task> command)
        {
            if (_disposed || _busy) return;
            BeginCommand();
            try { using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); await command(linked.Token); }
            catch (OperationCanceledException) when (_disposed) { }
            catch (Exception failure) when (failure is InvalidOperationException or ObjectDisposedException)
            { _commandStatus = "The request could not be completed. Retry the action."; }
            finally { EndCommand(); }
        }

        private void BeginCommand()
        {
            _commandDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _busy = true; _commandStatus = null; NotifyState();
        }

        private void EndCommand()
        {
            _busy = false; _commandDrained?.TrySetResult();
            if (!_disposed) NotifyState();
        }

        private static string? MutationMessage(PinMutationResult result, string success) => result.Status switch
        {
            PinMutationStatus.Saved or PinMutationStatus.Unchanged => success,
            PinMutationStatus.AlreadyPinned => "That application is already pinned.",
            PinMutationStatus.InvalidSource => result.Message ?? "That application target is not supported.",
            PinMutationStatus.ReadOnly or PinMutationStatus.Conflict or PinMutationStatus.SaveFailed => result.Message ?? "Pins could not be saved. Resolve the storage issue and reload pins.",
            _ => result.Message ?? "The pin change could not be completed. Retry the action."
        };

        private void NotifyProperty(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        private void NotifyState()
        {
            NotifyProperty(nameof(StatusText)); NotifyProperty(nameof(StatusVisibility));
        }

        public void Dispose() => _ = DisposeAsync();

        public ValueTask DisposeAsync()
        {
            if (_disposal is not null) return new(_disposal);
            _disposed = true;
            _windows.Changed -= OnWindowsChanged; _pins.Changed -= OnPinsChanged;
            _lifetime.Cancel();
            _disposal = DisposeServicesAsync();
            return new(_disposal);
        }

        private async Task DisposeServicesAsync()
        {
            if (_initialization is not null)
            {
                try { await _initialization; }
                catch (OperationCanceledException) { }
            }
            if (_commandDrained is not null) await _commandDrained.Task;
            if (_launchesDrained is not null) await _launchesDrained.Task;
            try { await _applications.DisposeAsync(); }
            finally
            {
                try { await _pins.DisposeAsync(); }
                finally { _windows.Dispose(); _lifetime.Dispose(); }
            }
        }
    }
}
