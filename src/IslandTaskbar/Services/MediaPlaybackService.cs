using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IslandTaskbar.Adapters.Interfaces;
using IslandTaskbar.Models;

namespace IslandTaskbar.Services
{
    /// <summary>Selects an active session and routes commands without simulated playback state.</summary>
    internal sealed class MediaPlaybackService : IDisposable
    {
        private readonly IMediaSessionService _adapter;
        private readonly object _gate = new();
        private MediaPlaybackSnapshot _current = new(null, null);
        private bool _disposed;

        public MediaPlaybackService(IMediaSessionService adapter)
        {
            _adapter = adapter;
            _adapter.SessionsChanged += OnSessionsChanged;
        }

        public event EventHandler? Changed;
        public MediaPlaybackSnapshot Current { get { lock (_gate) return _current; } }

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            await _adapter.InitializeAsync(cancellationToken).ConfigureAwait(false);
            Refresh();
        }

        private void OnSessionsChanged(object? sender, EventArgs e) => Refresh();

        private void Refresh()
        {
            bool changed;
            lock (_gate)
            {
                if (_disposed) return;
                var usable = _adapter.Sessions.Where(session => session.IsUsable).ToArray();
                var windowsCurrent = usable.FirstOrDefault(session => session.Id == _adapter.CurrentSessionId);
                var previous = usable.FirstOrDefault(session => session.Id == _current.Session?.Id);
                var selected = (windowsCurrent?.IsPlaying == true ? windowsCurrent : null)
                    ?? (previous?.IsPlaying == true ? previous : null)
                    ?? usable.FirstOrDefault(session => session.IsPlaying)
                    ?? windowsCurrent ?? previous ?? usable.FirstOrDefault();
                var next = new MediaPlaybackSnapshot(selected, _adapter.UnavailableReason);
                changed = next != _current;
                _current = next;
            }
            if (changed) Changed?.Invoke(this, EventArgs.Empty);
        }

        public Task<MediaCommandResult> ExecuteAsync(MediaCommand command, CancellationToken cancellationToken)
        {
            string? id;
            lock (_gate)
            {
                if (_disposed) return Task.FromResult(MediaCommandResult.Unavailable);
                id = _current.Session?.Id;
            }
            return ExecuteAsync(id, command, cancellationToken);
        }

        public Task<MediaCommandResult> ExecuteAsync(string? id, MediaCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MediaSessionState? clickedSession;
            lock (_gate)
            {
                if (_disposed) return Task.FromResult(MediaCommandResult.Unavailable);
                clickedSession = _adapter.Sessions.FirstOrDefault(session => session.Id == id && session.IsUsable);
            }
            if (clickedSession is null) return Task.FromResult(MediaCommandResult.Unavailable);
            if (!clickedSession.Supports(command)) return Task.FromResult(MediaCommandResult.Unsupported);
            // Capture identity now; completing a command cannot overwrite a newer selected session.
            return _adapter.ExecuteAsync(clickedSession.Id, command, cancellationToken);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _adapter.SessionsChanged -= OnSessionsChanged;
            }
            _adapter.Dispose();
        }
    }
}
