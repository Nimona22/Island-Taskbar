using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using IslandTaskbar.Models;
using IslandTaskbar.Services;

namespace IslandTaskbar.ViewModels
{
    /// <summary>Real session state with the existing decorative, illustrative music visualization.</summary>
    internal sealed class MusicPrototypeViewModel : INotifyPropertyChanged, IDisposable
    {
        private static readonly double[] IllustrativePattern =
            { .28, .46, .73, .95, .56, .38, .67, 1.0, .75, .43, .25 };
        private readonly MediaPlaybackService _media;
        private readonly DispatcherQueue _dispatcher;
        private readonly CancellationTokenSource _lifetime = new();
        private MediaPlaybackSnapshot _snapshot = new(null, null);
        private bool _busy;
        private bool _disposed;
        private string? _commandStatus;

        public MusicPrototypeViewModel(MediaPlaybackService media, DispatcherQueue dispatcher)
        {
            _media = media;
            _dispatcher = dispatcher;
            _media.Changed += OnMediaChanged;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        internal event EventHandler? MediaStateChanged;
        public bool IsPlaying => _snapshot.Session?.IsPlaying == true;
        public bool HasSession => _snapshot.Session is not null;
        public bool IsSessionTransitioning => _snapshot.IsDismissalPending;
        public string? SessionId => _snapshot.Session?.Id;
        public bool CanTogglePlayback => !_busy && _snapshot.Session?.Supports(MediaCommand.TogglePlayback) == true;
        public bool CanPrevious => !_busy && _snapshot.Session?.CanPrevious == true;
        public bool CanNext => !_busy && _snapshot.Session?.CanNext == true;
        public string StatusText => _commandStatus ?? _snapshot.UnavailableReason ??
            (HasSession ? $"{_snapshot.Session!.State}; waveform is illustrative" :
                IsSessionTransitioning ? "Waiting for media session" : "No active media session");
        public string PlayPauseName => IsPlaying ? "Pause media" : "Play media";
        public string PlayPauseGlyph => IsPlaying ? "\uE769" : "\uE768";
        internal int BarCount => IllustrativePattern.Length;

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            await _media.InitializeAsync(linked.Token);
        }

        private void OnMediaChanged(object? sender, EventArgs e)
        {
            // Read the latest snapshot inside the queued callback, avoiding out-of-order stale updates.
            _dispatcher.TryEnqueue(() =>
            {
                if (_disposed) return;
                _snapshot = _media.Current;
                _commandStatus = null;
                NotifyState();
                MediaStateChanged?.Invoke(this, EventArgs.Empty);
            });
        }

        public async Task ExecuteAsync(MediaCommand command, CancellationToken cancellationToken)
        {
            if (_disposed || _busy) return;
            string? clickedSessionId = _snapshot.Session?.Id;
            _busy = true;
            _commandStatus = null;
            NotifyState();
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
                var result = await _media.ExecuteAsync(clickedSessionId, command, linked.Token);
                if (_disposed) return;
                _commandStatus = result switch
                {
                    MediaCommandResult.Unsupported => "This media session does not support that command.",
                    MediaCommandResult.Unavailable => "The media session is no longer available.",
                    MediaCommandResult.Failed => "The media source did not accept the command.",
                    _ => null
                };
                if (result != MediaCommandResult.Success) Debug.WriteLine($"Media command {command}: {result}.");
            }
            catch (OperationCanceledException) when (_disposed || cancellationToken.IsCancellationRequested) { }
            finally
            {
                _busy = false;
                if (!_disposed) NotifyState();
            }
        }

        internal double GetIllustrativeScale(int bar, double phaseSeconds)
        {
            // Fixed design proportions, never audio levels. Track commands do not change this pattern.
            double modulation = .76 + .24 * Math.Sin(phaseSeconds * 4 + bar * .63);
            return Math.Max(.16, IllustrativePattern[bar] * modulation);
        }

        private void NotifyState()
        {
            foreach (string property in new[] { nameof(IsPlaying), nameof(HasSession), nameof(IsSessionTransitioning), nameof(SessionId),
                nameof(CanTogglePlayback), nameof(CanPrevious), nameof(CanNext), nameof(StatusText),
                nameof(PlayPauseName), nameof(PlayPauseGlyph) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _media.Changed -= OnMediaChanged;
            _lifetime.Cancel();
            _media.Dispose();
            _lifetime.Dispose();
        }
    }
}
