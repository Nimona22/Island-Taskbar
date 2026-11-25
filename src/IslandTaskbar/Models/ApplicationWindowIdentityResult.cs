namespace IslandTaskbar.Models
{
    /// <summary>
    /// Reliable enrichment of one exact live window (D-014). Available requires Identity;
    /// RelaunchReference is optional and offered only when trustworthy. No window enumeration or activation.
    /// </summary>
    internal sealed record ApplicationWindowIdentityResult(ApplicationResolutionStatus Status,
        ApplicationIdentity? Identity, LaunchReference? RelaunchReference, string? Message = null);
}

