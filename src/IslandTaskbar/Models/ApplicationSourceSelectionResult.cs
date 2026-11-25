namespace IslandTaskbar.Models
{
    internal enum ApplicationSourceSelectionStatus { Selected, Canceled, Unavailable, Failed }

    /// <summary>Source is non-null exactly when Selected; selection never launches or persists (D-014).</summary>
    internal sealed record ApplicationSourceSelectionResult(ApplicationSourceSelectionStatus Status,
        LaunchReference? Source, string? Message = null);
}

