using System.Collections.Immutable;

namespace IslandTaskbar.Models
{
    /// <summary>Immutable committed pin document; Pins must be initialized, even when empty (D-014).</summary>
    internal sealed record PinsData(int SchemaVersion, ImmutableArray<PinnedApp> Pins)
    {
        public const int CurrentSchemaVersion = 1;
    }
}

