using System.Collections.Immutable;

namespace IslandTaskbar.Models
{
    /// <summary>Immutable copied premultiplied BGRA pixels; never a native handle or persisted payload (D-014).</summary>
    internal sealed record ApplicationIcon(int Width, int Height, ImmutableArray<byte> Pixels);
}

