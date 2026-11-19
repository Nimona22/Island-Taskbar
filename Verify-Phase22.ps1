# Temporary Phase 2.2 runtime verification only. Does not modify application code or Windows settings.
$ErrorActionPreference = 'Stop'
$reportPath = Join-Path $PSScriptRoot 'Phase22.RuntimeResults.json'
$executable = Join-Path $PSScriptRoot 'src\IslandTaskbar\bin\x64\Debug\net10.0-windows10.0.26100.0\IslandTaskbar.exe'
$report = [ordered]@{ Date = (Get-Date).ToString('o'); Evidence = 'Native API measurements with manual close-button input'; Criteria = [ordered]@{} }

try {
    if (-not [Environment]::Is64BitProcess) { throw 'Run this script in 64-bit Windows PowerShell.' }
    if (-not (Test-Path -LiteralPath $executable)) { throw 'The Phase 2.2 Debug executable is missing.' }
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
public static class Phase22VerifyNative
{
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct Monitor { public int Size; public Rect Bounds, Work; public uint Flags; }
    public delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);
    public delegate bool WindowCallback(IntPtr window, IntPtr data);
    [DllImport("user32.dll", SetLastError=true)] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool GetMonitorInfoW(IntPtr monitor, ref Monitor info);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect rect, int size);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint thread, WindowCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowTextW(IntPtr window, StringBuilder text, int capacity);
    public static Monitor Primary()
    {
        Monitor result = new Monitor(); bool found = false;
        MonitorCallback callback = delegate(IntPtr handle, IntPtr dc, IntPtr rect, IntPtr data) {
            Monitor info = new Monitor(); info.Size = Marshal.SizeOf(typeof(Monitor));
            if (!GetMonitorInfoW(handle, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if ((info.Flags & 1) != 0) { result = info; found = true; }
            return true;
        };
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!found) throw new InvalidOperationException("No primary monitor was returned.");
        return result;
    }
    public static IntPtr ThreadWindow(uint thread)
    {
        IntPtr result = IntPtr.Zero;
        WindowCallback callback = delegate(IntPtr handle, IntPtr data) {
            StringBuilder title = new StringBuilder(256); GetWindowTextW(handle, title, title.Capacity);
            if (title.ToString() == "IslandTaskbar") { result = handle; return false; }
            return true;
        };
        EnumThreadWindows(thread, callback, IntPtr.Zero);
        return result;
    }
}
'@

    function Convert-Rect($rect) {
        [ordered]@{ Left = $rect.Left; Top = $rect.Top; Right = $rect.Right; Bottom = $rect.Bottom; Width = ($rect.Right - $rect.Left); Height = ($rect.Bottom - $rect.Top) }
    }
    function Get-Display {
        $oldContext = [Phase22VerifyNative]::SetThreadDpiAwarenessContext([IntPtr](-4))
        if ($oldContext -eq [IntPtr]::Zero) { throw 'Physical-pixel measurement context could not be established.' }
        try { [Phase22VerifyNative]::Primary() }
        finally { [Phase22VerifyNative]::SetThreadDpiAwarenessContext($oldContext) | Out-Null }
    }
    function Get-Foreground {
        $handle = [Phase22VerifyNative]::GetForegroundWindow()
        [uint32]$ownerId = 0
        [Phase22VerifyNative]::GetWindowThreadProcessId($handle, [ref]$ownerId) | Out-Null
        $owner = Get-Process -Id $ownerId -ErrorAction SilentlyContinue
        [ordered]@{ Handle = $handle.ToInt64(); ProcessId = $ownerId; ProcessName = $owner.ProcessName }
    }
    function Wait-ForButtonExit($process) {
        Write-Host "Click 'Close test window' in the diagnostic window now. Do not use End task or Alt+F4."
        if (-not $process.WaitForExit(600000)) { throw 'The diagnostic process did not exit within ten minutes.' }
        $process.WaitForExit()
        [ordered]@{ ProcessId = $process.Id; ExitCode = $process.ExitCode }
    }

    $version = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    $report.Environment = [ordered]@{ Windows = $version.DisplayVersion; Build = ($version.CurrentBuild + '.' + $version.UBR); Architecture = 'x64'; Executable = $executable }
    $existing = @(Get-Process -Name IslandTaskbar -ErrorAction SilentlyContinue)
    if ($existing.Count -gt 1) { throw 'There is more than one IslandTaskbar process. Close the extra diagnostic windows before retrying.' }
    if ($existing.Count -eq 1) {
        if ($existing[0].Path -ne $executable) { throw 'The running IslandTaskbar process is not the selected Debug build.' }
        $report.ExistingWindowClose = Wait-ForButtonExit $existing[0]
        $existing[0].Dispose()
    }

    Write-Host ''
    Write-Host 'STARTUP FOCUS CHECK: open a blank Notepad window, then return here.'
    Read-Host 'Press Enter when Notepad is ready' | Out-Null
    Write-Host 'Click inside Notepad during the next 10 seconds. Then leave mouse and keyboard untouched until the test window appears.'
    Start-Sleep -Seconds 10
    $beforeForeground = Get-Foreground
    $displayBefore = Get-Display
    $process = Start-Process -FilePath $executable -WorkingDirectory ([IO.Path]::GetDirectoryName($executable)) -PassThru
    $samples = @()
    $sampleDeadline = [DateTime]::UtcNow.AddSeconds(5)
    while ([DateTime]::UtcNow -lt $sampleDeadline -and -not $process.HasExited) {
        $samples += Get-Foreground
        Start-Sleep -Milliseconds 50
        $process.Refresh()
    }
    $becameForeground = @($samples | Where-Object { $_.ProcessId -eq $process.Id }).Count -gt 0
    $retainedForeground = $beforeForeground.Handle -ne 0 -and $samples.Count -gt 0 -and @($samples | Where-Object { $_.Handle -ne $beforeForeground.Handle }).Count -eq 0
    $report.Focus = [ordered]@{ Before = $beforeForeground; Samples = @($samples | ForEach-Object { [pscustomobject]$_ } | Select-Object Handle,ProcessId,ProcessName -Unique); SampleCount = $samples.Count; SamplingIntervalMilliseconds = 50; TestBecameForeground = $becameForeground; PreviousWindowRetainedForeground = $retainedForeground }
    $report.Criteria.StartupFocus = $(if ($becameForeground) { 'FAIL' } elseif ($retainedForeground) { 'PASS' } else { 'NOT VERIFIED' })

    $handle = [IntPtr]::Zero
    $handleDeadline = [DateTime]::UtcNow.AddSeconds(10)
    while ($handle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $handleDeadline -and -not $process.HasExited) {
        $process.Refresh(); $handle = $process.MainWindowHandle
        if ($handle -eq [IntPtr]::Zero) {
            foreach ($thread in $process.Threads) {
                $handle = [Phase22VerifyNative]::ThreadWindow([uint32]$thread.Id)
                if ($handle -ne [IntPtr]::Zero) { break }
            }
        }
        if ($handle -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 100 }
    }
    if ($handle -eq [IntPtr]::Zero) { throw 'No diagnostic window handle was found.' }

    $oldContext = [Phase22VerifyNative]::SetThreadDpiAwarenessContext([IntPtr](-4))
    if ($oldContext -eq [IntPtr]::Zero) { throw 'Could not establish physical-pixel context for the window measurements.' }
    try {
        $windowRect = New-Object Phase22VerifyNative+Rect
        if (-not [Phase22VerifyNative]::GetWindowRect($handle, [ref]$windowRect)) { throw 'GetWindowRect failed.' }
        $visibleRect = New-Object Phase22VerifyNative+Rect
        $dwmResult = [Phase22VerifyNative]::DwmGetWindowAttribute($handle, 9, [ref]$visibleRect, 16)
        $displayDuring = [Phase22VerifyNative]::Primary()
        $style = [Phase22VerifyNative]::GetWindowLongPtr($handle, -16).ToInt64()
        $extendedStyle = [Phase22VerifyNative]::GetWindowLongPtr($handle, -20).ToInt64()
    } finally { [Phase22VerifyNative]::SetThreadDpiAwarenessContext($oldContext) | Out-Null }

    $centerOffset = ($windowRect.Left + $windowRect.Right - $displayDuring.Bounds.Left - $displayDuring.Bounds.Right) / 2.0
    $topOffset = $windowRect.Top - $displayDuring.Bounds.Top
    $report.Geometry = [ordered]@{ WindowRect = (Convert-Rect $windowRect); VisibleFrameRect = $(if ($dwmResult -eq 0) { Convert-Rect $visibleRect } else { $null }); PrimaryOuterBounds = (Convert-Rect $displayDuring.Bounds); HorizontalCenterOffsetPixels = $centerOffset; TopOffsetPixels = $topOffset; WindowDpi = [Phase22VerifyNative]::GetDpiForWindow($handle); CaptionOrSizingBorderPresent = (($style -band 0x00C40000) -ne 0); TopmostStylePresent = (($extendedStyle -band 8) -ne 0) }
    $report.Criteria.HorizontalCenter = $(if ($centerOffset -eq 0) { 'PASS' } else { 'FAIL' })
    $report.Criteria.TopEdge = $(if ($topOffset -eq 0) { 'PASS' } else { 'FAIL' })
    # These additional style readings are diagnostics, not substitutes for WinUI visual/overlap checks.
    $report.Criteria.Borderless = 'NOT VERIFIED'
    $report.Criteria.Topmost = $(if ($report.Geometry.TopmostStylePresent) { 'PASS' } else { 'NOT VERIFIED' })
    $beforeWork = Convert-Rect $displayBefore.Work
    $duringWork = Convert-Rect $displayDuring.Work

    Write-Host ''
    Write-Host ('Measured window: ' + ($report.Geometry.WindowRect | ConvertTo-Json -Compress))
    Write-Host ('Center offset: ' + $centerOffset + ' px; top offset: ' + $topOffset + ' px')
    Write-Host ('Startup focus: ' + $report.Criteria.StartupFocus)
    $report.FinalWindowClose = Wait-ForButtonExit $process
    $report.Criteria.CloseButtonAndCleanExit = $(if ($report.FinalWindowClose.ExitCode -eq 0) { 'PASS' } else { 'FAIL' })
    $displayAfter = Get-Display
    $afterWork = Convert-Rect $displayAfter.Work
    $stableDisplay = ((Convert-Rect $displayBefore.Bounds | ConvertTo-Json -Compress) -eq (Convert-Rect $displayAfter.Bounds | ConvertTo-Json -Compress))
    $sameWorkArea = ($beforeWork | ConvertTo-Json -Compress) -eq ($duringWork | ConvertTo-Json -Compress) -and ($beforeWork | ConvertTo-Json -Compress) -eq ($afterWork | ConvertTo-Json -Compress)
    $report.WorkArea = [ordered]@{ BeforeLaunch = $beforeWork; DuringWindow = $duringWork; AfterClose = $afterWork; PrimaryDisplayUnchanged = $stableDisplay }
    $report.Criteria.WorkAreaUnchanged = $(if (-not $stableDisplay) { 'NOT VERIFIED' } elseif ($sameWorkArea) { 'PASS' } else { 'FAIL' })
    $report.RemainingTestProcessIds = @(Get-Process -Name IslandTaskbar -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
    if ($report.RemainingTestProcessIds.Count -ne 0) { $report.Criteria.CloseButtonAndCleanExit = 'FAIL' }
    $process.Dispose()
} catch {
    $report.Error = $_.Exception.Message
    Write-Host ('Verification error: ' + $report.Error) -ForegroundColor Red
} finally {
    foreach ($criterion in @('StartupFocus','HorizontalCenter','TopEdge','Borderless','Topmost','CloseButtonAndCleanExit','WorkAreaUnchanged')) {
        if (-not $report.Criteria.Contains($criterion)) { $report.Criteria[$criterion] = 'NOT VERIFIED' }
    }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    Write-Host ''
    Write-Host ($report.Criteria | ConvertTo-Json)
    Write-Host ('Results saved to: ' + $reportPath)
    Read-Host 'Press Enter to close this verification console' | Out-Null
}
