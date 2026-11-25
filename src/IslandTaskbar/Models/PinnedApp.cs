namespace IslandTaskbar.Models
{
    /// <summary>Persisted v1 data only. New pins use null IconSource; Order is contiguous from zero (D-014).</summary>
    internal sealed record PinnedApp(PinId Id, string DisplayName, LaunchReference LaunchReference,
        string? IconSource, int Order);
}

