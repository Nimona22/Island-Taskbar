namespace IslandTaskbar.Models
{
    internal sealed record RunningWindow(RunningWindowId Id, string Title, string ProcessName,
        string? ExecutablePath, bool IsMinimized, bool IsActive, WindowIcon? Icon);
}
