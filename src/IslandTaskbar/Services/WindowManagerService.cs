using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using IslandTaskbar.Adapters.Interfaces;
using IslandTaskbar.Models;

namespace IslandTaskbar.Services
{
    internal sealed class WindowManagerService : IDisposable
    {
        private readonly IWindowSystem _adapter;
        private readonly object _gate = new();
        private readonly CancellationTokenSource _lifetime = new();
        private readonly CancellationToken _token;
        private readonly Timer _refreshTimer;
        private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(80);
        private IReadOnlyList<RunningWindow> _current = Array.Empty<RunningWindow>();
        private string? _unavailableReason;
        private bool _refreshing;
        private bool _pending;
        private bool _disposed;

        public WindowManagerService(IWindowSystem adapter)
        {
            _adapter = adapter;
            _token = _lifetime.Token;
            _refreshTimer = new Timer(_ => { _ = RefreshAsync(_token); }, null, Timeout.Infinite, Timeout.Infinite);
            _adapter.WindowsChanged += OnWindowsChanged;
        }

        public event EventHandler? Changed;
        public IReadOnlyList<RunningWindow> Current { get { lock (_gate) return _current; } }
        public string? UnavailableReason { get { lock (_gate) return _unavailableReason; } }

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Out-of-context hooks use the caller's existing WinUI message pump.
                _adapter.Initialize();
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _token);
                await RefreshAsync(linked.Token).ConfigureAwait(false);
            }
            catch (Exception failure) when (IsWindowFailure(failure)) { ReportFailure(failure); }
        }

        private void OnWindowsChanged(object? sender, EventArgs e)
        {
            lock (_gate)
            {
                if (_disposed) return;
                bool alreadyScheduled = _pending;
                _pending = true;
                // Coalesce bursts; while enumeration is in flight, it consumes this flag itself.
                if (!_refreshing && !alreadyScheduled) _refreshTimer.Change(RefreshDelay, Timeout.InfiniteTimeSpan);
            }
        }

        private async Task RefreshAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (_disposed) return;
                if (_refreshing) { _pending = true; return; }
                _refreshing = true;
            }
            try
            {
                while (true)
                {
                    lock (_gate) _pending = false;
                    var incoming = await _adapter.EnumerateAsync(cancellationToken).ConfigureAwait(false);
                    bool changed;
                    lock (_gate)
                    {
                        if (_disposed) return;
                        var byId = incoming.ToDictionary(window => window.Id);
                        var next = new List<RunningWindow>();
                        foreach (var window in _current)
                            if (byId.Remove(window.Id, out var updated)) next.Add(updated);
                        foreach (var window in incoming)
                            if (byId.Remove(window.Id)) next.Add(window);
                        changed = _unavailableReason is not null || !_current.SequenceEqual(next);
                        _current = next.ToArray();
                        _unavailableReason = null;
                    }
                    if (changed) Changed?.Invoke(this, EventArgs.Empty);
                    lock (_gate)
                    {
                        if (_disposed || !_pending) return;
                    }
                    await Task.Delay(RefreshDelay, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_token.IsCancellationRequested || cancellationToken.IsCancellationRequested) { }
            catch (Exception failure) when (IsWindowFailure(failure)) { ReportFailure(failure); }
            finally
            {
                lock (_gate)
                {
                    _refreshing = false;
                    if (!_disposed && _pending) _refreshTimer.Change(RefreshDelay, Timeout.InfiniteTimeSpan);
                }
            }
        }

        public async Task<WindowActivationResult> ActivateAsync(RunningWindow window, CancellationToken cancellationToken)
        {
            lock (_gate)
                if (_disposed || !_current.Any(current => current.Id == window.Id)) return WindowActivationResult.Unavailable;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _token);
            var result = await _adapter.ActivateAsync(window, linked.Token).ConfigureAwait(false);
            OnWindowsChanged(this, EventArgs.Empty);
            return result;
        }

        private static bool IsWindowFailure(Exception failure) => failure is Win32Exception or COMException or InvalidOperationException;

        private void ReportFailure(Exception failure)
        {
            Debug.WriteLine($"Window discovery failed: {failure.Message}");
            lock (_gate)
            {
                if (_disposed) return;
                _unavailableReason = "Windows applications are temporarily unavailable.";
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }
            _adapter.WindowsChanged -= OnWindowsChanged;
            _refreshTimer.Dispose();
            _lifetime.Cancel();
            _adapter.Dispose();
            _lifetime.Dispose();
        }
    }
}
