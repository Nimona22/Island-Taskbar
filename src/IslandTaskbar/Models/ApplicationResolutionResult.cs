namespace IslandTaskbar.Models
{
    internal enum ApplicationResolutionStatus { Available, Missing, Unsupported, AccessDenied, Failed }

    /// <summary>Application is non-null exactly when Status is Available; Message is safe for UI display (D-014).</summary>
    internal sealed record ApplicationResolutionResult(ApplicationResolutionStatus Status,
        ApplicationDetails? Application, string? Message = null);
}

