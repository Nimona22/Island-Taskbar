using System.Collections.Immutable;

namespace IslandTaskbar.Models
{
    /// <summary>
    /// One immutable service snapshot (D-014). Runtime state is separate from PinsData and keyed by persistent ID.
    /// Collections are initialized and runtime keys belong to Pins. A failure retains the last committed Pins.
    /// </summary>
    internal sealed record PinManagerSnapshot(bool IsInitialized, ImmutableArray<PinnedApp> Pins,
        ImmutableDictionary<PinId, PinnedAppRuntimeState> RuntimeStates, PinsWriteAccess WriteAccess,
        string? Revision, string? Message = null);
}

