namespace IslandTaskbar.Models
{
    internal enum MediaPlaybackState { Unknown, Playing, Paused, Stopped, Closed }
    internal enum MediaCommand { TogglePlayback, Previous, Next }
    internal enum MediaCommandResult { Success, Unsupported, Unavailable, Failed }

    /// <summary>Playback and command capabilities only; contains no track metadata.</summary>
    internal sealed record MediaSessionState(string Id, string AppId, MediaPlaybackState State,
        bool CanPlay, bool CanPause, bool CanToggle, bool CanPrevious, bool CanNext)
    {
        public bool IsPlaying => State == MediaPlaybackState.Playing;
        public bool IsUsable => State != MediaPlaybackState.Closed &&
            (IsPlaying || State == MediaPlaybackState.Paused || CanPlay || CanToggle);
        public bool Supports(MediaCommand command) => command switch
        {
            MediaCommand.TogglePlayback => CanToggle || (IsPlaying ? CanPause : CanPlay),
            MediaCommand.Previous => CanPrevious,
            MediaCommand.Next => CanNext,
            _ => false
        };
    }

    internal sealed record MediaPlaybackSnapshot(MediaSessionState? Session, string? UnavailableReason);
}
