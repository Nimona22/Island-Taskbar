namespace IslandTaskbar.Models
{
    internal enum PinsLoadStatus { Loaded, Missing, Corrupt, UnsupportedVersion, AccessDenied, Failed }
    internal enum PinsWriteAccess { ReadOnly, Writable }

    /// <summary>
    /// Loaded/Missing carry Data. Missing has empty v1 Data and null Revision.
    /// Invalid/unreadable data is null and read-only. Revision is an opaque raw-byte token (D-014).
    /// </summary>
    internal sealed record PinsLoadResult(PinsLoadStatus Status, PinsData? Data, string? Revision,
        PinsWriteAccess WriteAccess, string? Message = null);
}

