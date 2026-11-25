namespace IslandTaskbar.Models
{
    /// <summary>
    /// Runtime resolution payload (D-014). Identity may be unknown, particularly for profile/argument shortcuts.
    /// A missing icon does not make an otherwise valid target unavailable.
    /// </summary>
    internal sealed record ApplicationDetails(LaunchReference LaunchReference, string DisplayName,
        ApplicationIdentity? Identity, ApplicationIcon? Icon);
}

