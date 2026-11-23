using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;
using IslandTaskbar.Adapters.Interfaces;
using IslandTaskbar.Models;

namespace IslandTaskbar.Adapters.Implementations
{
    /// <summary>Event-driven GSMTC adapter. Does not request artwork or other media properties.</summary>
    internal sealed class WindowsMediaSessionService : IMediaSessionService
    {
        private sealed record TrackedSession(string Id, GlobalSystemMediaTransportControlsSession Session);
        private readonly object _gate = new();
        private static readonly Guid IUnknownId = new("00000000-0000-0000-C000-000000000046");
        private readonly List<TrackedSession> _tracked = new();
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private IReadOnlyList<MediaSessionState> _sessions = Array.Empty<MediaSessionState>();
        private string? _currentSessionId;
        private string? _unavailableReason;
        private long _nextId;
        private bool _disposed;

        public event EventHandler? SessionsChanged;
        public IReadOnlyList<MediaSessionState> Sessions { get { lock (_gate) return _sessions; } }
        public string? CurrentSessionId { get { lock (_gate) return _currentSessionId; } }
        public string? UnavailableReason { get { lock (_gate) return _unavailableReason; } }

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            try
            {
                var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync()
                    .AsTask(cancellationToken).ConfigureAwait(false);
                lock (_gate)
                {
                    if (_disposed) return;
                    _manager = manager;
                    manager.SessionsChanged += OnSessionsChanged;
                    manager.CurrentSessionChanged += OnCurrentSessionChanged;
                }
                Refresh();
            }
            catch (Exception failure) when (IsMediaFailure(failure))
            {
                ReportFailure("Request GSMTC manager", failure);
                lock (_gate)
                {
                    if (_disposed) return;
                    _unavailableReason = "Windows media sessions are unavailable.";
                }
                SessionsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) => Refresh();
        private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) => Refresh();
        private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => Refresh();
        private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => Refresh();

        private void Refresh()
        {
            lock (_gate)
            {
                if (_disposed || _manager is null) return;
                try
                {
                    var available = _manager.GetSessions();
                    foreach (var removed in _tracked.Where(entry => !available.Any(session => SameSession(session, entry.Session))).ToArray())
                    {
                        Unsubscribe(removed);
                        _tracked.Remove(removed);
                    }
                    foreach (var session in available)
                    {
                        if (_tracked.Any(entry => SameSession(entry.Session, session))) continue;
                        session.PlaybackInfoChanged += OnPlaybackInfoChanged;
                        var tracked = new TrackedSession((++_nextId).ToString(System.Globalization.CultureInfo.InvariantCulture), session);
                        try { session.TimelinePropertiesChanged += OnTimelinePropertiesChanged; }
                        catch
                        {
                            // Do not retain a partially subscribed session; a later refresh can retry it.
                            Unsubscribe(tracked);
                            throw;
                        }
                        _tracked.Add(tracked);
                    }
                    var current = _manager.GetCurrentSession();
                    _currentSessionId = _tracked.FirstOrDefault(entry => SameSession(entry.Session, current))?.Id;
                    var states = new List<MediaSessionState>();
                    foreach (var entry in _tracked)
                    {
                        try { states.Add(ReadState(entry)); }
                        catch (Exception failure) when (IsMediaFailure(failure))
                        {
                            // A session may disappear between enumeration and its playback-info read.
                            ReportFailure("Read GSMTC playback state", failure);
                        }
                    }
                    if (_currentSessionId is null && current is not null)
                    {
                        // Some providers return a separate current-session proxy with different COM identity.
                        // Resolve by publisher only when unique; never guess among sessions from the same app.
                        string currentAppId = current.SourceAppUserModelId;
                        var matches = states.Where(state => state.AppId == currentAppId).ToArray();
                        if (matches.Length == 1) _currentSessionId = matches[0].Id;
                    }
                    _sessions = states.ToArray();
                    _unavailableReason = null;
                }
                catch (Exception failure) when (IsMediaFailure(failure))
                {
                    ReportFailure("Refresh GSMTC sessions", failure);
                    _sessions = Array.Empty<MediaSessionState>();
                    _currentSessionId = null;
                    _unavailableReason = "Windows media sessions are unavailable.";
                }
            }
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }

        private static bool SameSession(GlobalSystemMediaTransportControlsSession left,
            GlobalSystemMediaTransportControlsSession? right)
        {
            if (right is null) return false;
            // GetCurrentSession/GetSessions can project different managed wrappers for the same COM object.
            using var leftIdentity = ((WinRT.IWinRTObject)left).NativeObject.As<WinRT.Interop.IUnknownVftbl>(IUnknownId);
            using var rightIdentity = ((WinRT.IWinRTObject)right).NativeObject.As<WinRT.Interop.IUnknownVftbl>(IUnknownId);
            return leftIdentity.ThisPtr == rightIdentity.ThisPtr;
        }

        private static MediaSessionState ReadState(TrackedSession entry)
        {
            var info = entry.Session.GetPlaybackInfo();
            var controls = info.Controls;
            var state = info.PlaybackStatus switch
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackState.Playing,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackState.Paused,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => MediaPlaybackState.Stopped,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => MediaPlaybackState.Closed,
                _ => MediaPlaybackState.Unknown
            };
            if (state == MediaPlaybackState.Paused)
            {
                try
                {
                    var timeline = entry.Session.GetTimelineProperties();
                    state = MediaSessionState.ResolvePlaybackState(state, timeline.StartTime, timeline.EndTime, timeline.Position);
                }
                catch (Exception failure) when (IsMediaFailure(failure))
                {
                    // Unsupported/missing timeline is not evidence of completion; retain a known pause.
                    ReportFailure("Read GSMTC completion timeline", failure);
                }
            }
            return new MediaSessionState(entry.Id, entry.Session.SourceAppUserModelId, state,
                controls.IsPlayEnabled, controls.IsPauseEnabled, controls.IsPlayPauseToggleEnabled,
                controls.IsPreviousEnabled, controls.IsNextEnabled);
        }

        public async Task<MediaCommandResult> ExecuteAsync(string id, MediaCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TrackedSession? target;
            lock (_gate)
            {
                if (_disposed) return MediaCommandResult.Unavailable;
                target = _tracked.FirstOrDefault(entry => entry.Id == id);
            }
            if (target is null) return MediaCommandResult.Unavailable;
            try
            {
                // Recheck capabilities on the captured session, without selecting a different one.
                var state = ReadState(target);
                cancellationToken.ThrowIfCancellationRequested();
                if (!state.IsUsable) return MediaCommandResult.Unavailable;
                if (!state.Supports(command)) return MediaCommandResult.Unsupported;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                timeout.Token.ThrowIfCancellationRequested();
                var operation = command switch
                {
                    MediaCommand.Previous => target.Session.TrySkipPreviousAsync(),
                    MediaCommand.Next => target.Session.TrySkipNextAsync(),
                    _ when state.CanToggle => target.Session.TryTogglePlayPauseAsync(),
                    _ when state.IsPlaying => target.Session.TryPauseAsync(),
                    _ => target.Session.TryPlayAsync()
                };
                bool accepted = await operation.AsTask(timeout.Token).ConfigureAwait(false);
                Refresh();
                return accepted ? MediaCommandResult.Success : MediaCommandResult.Failed;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Debug.WriteLine("GSMTC command timed out; awaiting real session state.");
                return MediaCommandResult.Failed;
            }
            catch (Exception failure) when (IsMediaFailure(failure))
            {
                ReportFailure("Execute GSMTC command", failure);
                Refresh();
                return MediaCommandResult.Unavailable;
            }
        }

        private static bool IsMediaFailure(Exception failure) => failure is COMException
            or UnauthorizedAccessException or InvalidOperationException;
        private static void ReportFailure(string operation, Exception failure) =>
            Debug.WriteLine($"{operation}: {failure.GetType().Name} (0x{failure.HResult:X8}).");

        private void Unsubscribe(TrackedSession entry)
        {
            try { entry.Session.PlaybackInfoChanged -= OnPlaybackInfoChanged; }
            catch (Exception failure) when (IsMediaFailure(failure)) { ReportFailure("Unsubscribe GSMTC session", failure); }
            try { entry.Session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged; }
            catch (Exception failure) when (IsMediaFailure(failure)) { ReportFailure("Unsubscribe GSMTC timeline", failure); }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                if (_manager is not null)
                {
                    _manager.SessionsChanged -= OnSessionsChanged;
                    _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
                    _manager = null;
                }
                foreach (var entry in _tracked) Unsubscribe(entry);
                _tracked.Clear();
                _sessions = Array.Empty<MediaSessionState>();
                _currentSessionId = null;
            }
        }
    }
}
