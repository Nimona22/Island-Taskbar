using System;
using System.Collections.Generic;
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
    internal sealed class TaskbarViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly WindowManagerService _windows;
        private readonly DispatcherQueue _dispatcher;
        private readonly CancellationTokenSource _lifetime = new();
        private bool _busy;
        private bool _disposed;
        private string? _commandStatus;

        internal TaskbarViewModel(WindowManagerService windows, DispatcherQueue dispatcher)
        {
            _windows = windows;
            _dispatcher = dispatcher;
            _windows.Changed += OnWindowsChanged;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public ObservableCollection<RunningWindowItemViewModel> Windows { get; } = new();
        public string StatusText => _busy ? "Switching window…" : _commandStatus ?? _windows.UnavailableReason ?? (Windows.Count == 0 ? "No running applications" : "");
        public Visibility StatusVisibility => string.IsNullOrEmpty(StatusText) ? Visibility.Collapsed : Visibility.Visible;

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            await _windows.InitializeAsync(linked.Token);
            if (!_disposed) Reconcile();
        }

        private void OnWindowsChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(() =>
        {
            if (_disposed) return;
            Reconcile();
        });

        private void Reconcile()
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

        public async Task<bool> ActivateAsync(RunningWindowItemViewModel entry, CancellationToken cancellationToken)
        {
            if (_disposed || _busy) return false;
            _busy = true;
            _commandStatus = null;
            NotifyState();
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
            finally { _busy = false; if (!_disposed) NotifyState(); }
        }

        private void NotifyState()
        {
            foreach (string property in new[] { nameof(StatusText), nameof(StatusVisibility) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _windows.Changed -= OnWindowsChanged;
            _lifetime.Cancel();
            _windows.Dispose();
            _lifetime.Dispose();
        }
    }
}
