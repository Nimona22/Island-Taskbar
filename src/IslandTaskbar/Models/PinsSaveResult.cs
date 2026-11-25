namespace IslandTaskbar.Models
{
    internal enum PinsSaveStatus { Saved, Unchanged, ReadOnly, Conflict, AccessDenied, Failed }

    /// <summary>Only Saved/Unchanged return a committed Revision; failure never publishes proposed data (D-014).</summary>
    internal sealed record PinsSaveResult(PinsSaveStatus Status, string? Revision, string? Message = null);
}

