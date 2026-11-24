namespace IslandTaskbar.Models
{
    internal sealed record WindowCandidate(nint Handle, uint ProcessId, uint ThreadId, string Title,
        string ClassName, bool IsVisible, bool IsToolWindow, bool IsCloaked, bool IsOwned,
        bool IsAppWindow, bool IsNoActivate, bool IsMinimized = false);
}
