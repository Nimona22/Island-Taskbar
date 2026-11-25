namespace IslandTaskbar.Models
{
    internal enum ApplicationLaunchStatus
    {
        Accepted,
        Busy,
        Canceled,
        Unavailable,
        StaleTarget,
        Unsupported,
        Denied,
        Failed,
        OutcomeUnknown
    }

    /// <summary>
    /// Accepted means dispatch accepted, not a new window. Canceled proves no dispatch;
    /// OutcomeUnknown never authorizes an automatic retry (D-014).
    /// </summary>
    internal sealed record ApplicationLaunchResult(ApplicationLaunchStatus Status, string? Message = null);
}

