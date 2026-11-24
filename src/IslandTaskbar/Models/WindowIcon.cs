namespace IslandTaskbar.Models
{
    /// <summary>Copied premultiplied BGRA pixels; no borrowed native handle crosses this boundary.</summary>
    internal sealed record WindowIcon(int Width, int Height, byte[] Pixels);
}
