using System;
using System.Collections.Generic;
using IslandTaskbar.Models;

namespace IslandTaskbar.Adapters.Implementations
{
    internal static class WindowEligibility
    {
        private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "MultitaskingViewFrame",
            "TaskListThumbnailWnd", "Shell_InputSwitchTopLevelWindow", "ApplicationManager_DesktopShellWindow",
            "XamlExplorerHostIslandWindow", "tooltips_class32"
        };

        internal static bool IsEligible(WindowCandidate window, uint ownProcessId)
        {
            return window.Handle != 0 && window.ProcessId != 0 && window.ProcessId != ownProcessId
                && window.IsVisible && !window.IsToolWindow && !window.IsCloaked && !window.IsNoActivate
                && (!window.IsOwned || window.IsAppWindow) && !ShellClasses.Contains(window.ClassName)
                && (!string.IsNullOrWhiteSpace(window.Title) || window.IsAppWindow);
        }
    }
}
