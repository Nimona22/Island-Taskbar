namespace IslandTaskbar.Models
{
    internal enum ApplicationPresence { Unknown, Running }

    /// <summary>
    /// Non-persisted state keyed by PinId (D-014). Null Resolution means not yet checked.
    /// Unknown includes no reliable positive match; it never means zero running windows.
    /// </summary>
    internal sealed record PinnedAppRuntimeState(ApplicationResolutionResult? Resolution,
        ApplicationPresence Presence);
}

