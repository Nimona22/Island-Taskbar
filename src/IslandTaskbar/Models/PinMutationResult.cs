namespace IslandTaskbar.Models
{
    internal enum PinMutationStatus
    {
        Saved,
        Unchanged,
        AlreadyPinned,
        NotFound,
        NotInitialized,
        InvalidSource,
        ReadOnly,
        Conflict,
        SaveFailed
    }

    /// <summary>
    /// Saved/Unchanged/AlreadyPinned identify the affected pin. InvalidSource carries resolution status.
    /// Expected operational failures are values, not silent no-ops (D-014).
    /// </summary>
    internal sealed record PinMutationResult(PinMutationStatus Status, PinId? PinId = null,
        ApplicationResolutionStatus? ResolutionStatus = null, string? Message = null);
}

