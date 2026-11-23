using System;

namespace IslandTaskbar.Models
{
    internal enum MediaPlaybackState { Unknown, Playing, Paused, Stopped, Closed, Ended }
    internal enum MediaCommand { TogglePlayback, Previous, Next }
    internal enum MediaCommandResult { Success, Unsupported, Unavailable, Failed }

    /// <summary>Playback and command capabilities only; contains no track metadata.</summary>
    internal sealed record MediaSessionState(string Id, string AppId, MediaPlaybackState State,
        bool CanPlay, bool CanPause, bool CanToggle, bool CanPrevious, bool CanNext)
    {
        public bool IsPlaying => State == MediaPlaybackState.Playing;
        // Command capabilities can remain enabled after playback stops; they are not availability.
        public bool IsUsable => State is MediaPlaybackState.Playing or MediaPlaybackState.Paused;

        internal static MediaPlaybackState ResolvePlaybackState(MediaPlaybackState state,
            TimeSpan startTime, TimeSpan endTime, TimeSpan position)
        {
            // GSMTC has no Ended status. Use an explicit, known endpoint only when not playing.
            // Unknown/live durations and stale endpoints while playing must not dismiss the panel.
            return state == MediaPlaybackState.Paused && endTime > startTime && position >= endTime
                ? MediaPlaybackState.Ended : state;
        }
        public bool Supports(MediaCommand command) => command switch
        {
            MediaCommand.TogglePlayback => CanToggle || (IsPlaying ? CanPause : CanPlay),
            MediaCommand.Previous => CanPrevious,
            MediaCommand.Next => CanNext,
            _ => false
        };
    }

    internal sealed record MediaPlaybackSnapshot(MediaSessionState? Session, string? UnavailableReason,
        bool IsDismissalPending = false);
}
