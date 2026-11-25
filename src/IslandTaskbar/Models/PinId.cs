using System;

namespace IslandTaskbar.Models
{
    /// <summary>A persistent launcher key; Guid.Empty is invalid at persistence/service boundaries (D-014).</summary>
    internal readonly record struct PinId(Guid Value);
}

