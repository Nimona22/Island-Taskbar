namespace IslandTaskbar.Models
{
    internal readonly record struct RunningWindowId(nint Handle, uint ProcessId, uint ThreadId, long Instance);
}
