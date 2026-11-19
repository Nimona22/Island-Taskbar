# Phase 2.3 verification only: queries window regions/input routing; does not inject input or alter Windows settings.
$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot 'src\IslandTaskbar\bin\x64\Debug\net10.0-windows10.0.26100.0\IslandTaskbar.exe'
$resultPath = Join-Path $PSScriptRoot 'Phase23.RuntimeResults.json'
$result = [ordered]@{ Phase = '2.3'; Date = (Get-Date).ToString('o'); Criteria = [ordered]@{} }
$region = [IntPtr]::Zero
$oldDpiContext = [IntPtr]::Zero
try {
    if (-not [Environment]::Is64BitProcess) { throw 'Use 64-bit Windows PowerShell.' }
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
public static class Phase23Probe
{
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Monitor { public int Size; public Rect Bounds, Work; public uint Flags; }
    public delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);
    public delegate bool WindowCallback(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool GetMonitorInfoW(IntPtr monitor, ref Monitor info);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint owner);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("user32.dll")] public static extern int GetWindowRgn(IntPtr window, IntPtr region);
    [DllImport("gdi32.dll")] public static extern int GetRgnBox(IntPtr region, out Rect rect);
    [DllImport("gdi32.dll")] public static extern bool PtInRegion(IntPtr region, int x, int y);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
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
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        if (!found) throw new InvalidOperationException("No primary monitor found.");
        return result;
    }
    public static IntPtr ThreadWindow(uint thread)
    {
        IntPtr result = IntPtr.Zero;
        WindowCallback callback = delegate(IntPtr handle, IntPtr data) {
            StringBuilder text = new StringBuilder(256); GetWindowTextW(handle, text, text.Capacity);
            if (text.ToString() == "IslandTaskbar") { result = handle; return false; }
            return true;
        };
        EnumThreadWindows(thread, callback, IntPtr.Zero); return result;
    }
}
'@
    function Get-Foreground {
        $handle = [Phase23Probe]::GetForegroundWindow()
        [uint32]$ownerId = 0
        [Phase23Probe]::GetWindowThreadProcessId($handle, [ref]$ownerId) | Out-Null
        $owner = Get-Process -Id $ownerId -ErrorAction SilentlyContinue
        [ordered]@{ Handle = $handle.ToInt64(); ProcessId = $ownerId; Name = $owner.ProcessName }
    }
    function Get-Owner([IntPtr]$handle) {
        [uint32]$ownerId = 0
        [Phase23Probe]::GetWindowThreadProcessId($handle, [ref]$ownerId) | Out-Null
        return $ownerId
    }
    function Convert-Rect($rect) {
        [ordered]@{ Left = $rect.Left; Top = $rect.Top; Right = $rect.Right; Bottom = $rect.Bottom; Width = ($rect.Right - $rect.Left); Height = ($rect.Bottom - $rect.Top) }
    }
    function Wait-ForClick([bool]$inside) {
        $deadline = [DateTime]::UtcNow.AddMinutes(10)
        $wasDown = (([Phase23Probe]::GetAsyncKeyState(1) -band 0x8000) -ne 0)
        while ([DateTime]::UtcNow -lt $deadline) {
            if ($testProcess.HasExited) { throw 'The diagnostic process exited before the input check finished.' }
            $isDown = (([Phase23Probe]::GetAsyncKeyState(1) -band 0x8000) -ne 0)
            if ($isDown -and -not $wasDown) {
                $cursor = New-Object Phase23Probe+Point
                if ([Phase23Probe]::GetCursorPos([ref]$cursor)) {
                    $inIsland = [Phase23Probe]::PtInRegion($region, $cursor.X - $windowRect.Left, $cursor.Y - $windowRect.Top)
                    $hitOwner = Get-Owner ([Phase23Probe]::WindowFromPoint($cursor))
                    if (($inside -and $inIsland -and $hitOwner -eq $testProcess.Id) -or (-not $inside -and -not $inIsland -and $hitOwner -eq $notepad.ProcessId)) {
                        while (([Phase23Probe]::GetAsyncKeyState(1) -band 0x8000) -ne 0) { Start-Sleep -Milliseconds 20 }
                        Start-Sleep -Milliseconds 250
                        return [ordered]@{ X = $cursor.X; Y = $cursor.Y; InsideRegion = $inIsland; PointOwnerProcessId = $hitOwner; ForegroundAfter = (Get-Foreground) }
                    }
                }
            }
            $wasDown = $isDown
            Start-Sleep -Milliseconds 20
        }
        throw 'No qualifying manual click was detected within ten minutes.'
    }

    $oldDpiContext = [Phase23Probe]::SetThreadDpiAwarenessContext([IntPtr](-4))
    if ($oldDpiContext -eq [IntPtr]::Zero) { throw 'Could not establish physical-pixel query context.' }
    $existing = @(Get-Process -Name IslandTaskbar -ErrorAction SilentlyContinue)
    if ($existing.Count -gt 1) { throw 'Close extra IslandTaskbar windows before retrying.' }
    if ($existing.Count -eq 1) {
        if ($existing[0].Path -ne $executable) { throw 'A different IslandTaskbar executable is already running.' }
        Write-Host "Close the old diagnostic window with 'Close test window' now."
        if (-not $existing[0].WaitForExit(600000)) { throw 'Old diagnostic window did not close.' }
        $existing[0].Dispose()
    }
    Write-Host 'Open a blank Notepad window, maximize it, and click inside the document.'
    Write-Host 'The Phase 2.3 window will launch automatically. Wait 10 seconds after it appears before clicking it.'
    $readyDeadline = [DateTime]::UtcNow.AddMinutes(10)
    $notepad = $null
    while ([DateTime]::UtcNow -lt $readyDeadline) {
        $candidate = Get-Foreground
        if ($candidate.Name -eq 'Notepad' -and [Phase23Probe]::IsZoomed([IntPtr]$candidate.Handle)) { $notepad = $candidate; break }
        Start-Sleep -Milliseconds 100
    }
    if (-not $notepad) { throw 'A maximized foreground Notepad window was not observed.' }
    $displayBefore = [Phase23Probe]::Primary()
    $testProcess = Start-Process -FilePath $executable -WorkingDirectory ([IO.Path]::GetDirectoryName($executable)) -PassThru
    $region = [Phase23Probe]::CreateRectRgn(0, 0, 0, 0)
    if ($region -eq [IntPtr]::Zero) { throw 'Could not allocate the query region.' }
    $windowHandle = [IntPtr]::Zero
    $regionType = 0
    $regionReadyDeadline = [DateTime]::UtcNow.AddSeconds(15)
    $regionBox = New-Object Phase23Probe+Rect
    while ([DateTime]::UtcNow -lt $regionReadyDeadline -and -not $testProcess.HasExited) {
        $testProcess.Refresh(); $windowHandle = $testProcess.MainWindowHandle
        if ($windowHandle -eq [IntPtr]::Zero) {
            foreach ($thread in $testProcess.Threads) {
                $windowHandle = [Phase23Probe]::ThreadWindow([uint32]$thread.Id)
                if ($windowHandle -ne [IntPtr]::Zero) { break }
            }
        }
        if ($windowHandle -ne [IntPtr]::Zero) {
            $regionType = [Phase23Probe]::GetWindowRgn($windowHandle, $region)
            if ($regionType -gt 1 -and [Phase23Probe]::GetRgnBox($region, [ref]$regionBox) -gt 1) { break }
        }
        Start-Sleep -Milliseconds 100
    }
    if ($testProcess.HasExited) { $testProcess.WaitForExit(); throw ('The prototype exited before displaying its island. Exit code: ' + $testProcess.ExitCode) }
    if ($regionType -le 1) { throw 'A visible native island region did not appear within fifteen seconds.' }
    $windowRect = New-Object Phase23Probe+Rect
    if (-not [Phase23Probe]::GetWindowRect($windowHandle, [ref]$windowRect)) { throw 'GetWindowRect failed.' }
    $displayDuring = [Phase23Probe]::Primary()
    $width = $windowRect.Right - $windowRect.Left
    $height = $windowRect.Bottom - $windowRect.Top
    $centerX = [int](($regionBox.Left + $regionBox.Right) / 2)
    $sampleY = [int](($regionBox.Top + $regionBox.Bottom) * 0.75)
    $points = @(
        @{ Name = 'Inside'; X = $centerX; Y = [int](($regionBox.Top + $regionBox.Bottom) / 2); ExpectInside = $true },
        @{ Name = 'Left margin'; X = [int]($regionBox.Left / 2); Y = $sampleY; ExpectInside = $false },
        @{ Name = 'Right margin'; X = [int](($regionBox.Right + $width) / 2); Y = $sampleY; ExpectInside = $false },
        @{ Name = 'Below island'; X = $centerX; Y = [int](($regionBox.Bottom + $height) / 2); ExpectInside = $false },
        @{ Name = 'Lower-left corner cutout'; X = ($regionBox.Left + 1); Y = ($regionBox.Bottom - 1); ExpectInside = $false },
        @{ Name = 'Lower-right corner cutout'; X = ($regionBox.Right - 2); Y = ($regionBox.Bottom - 1); ExpectInside = $false }
    )
    $routing = @()
    foreach ($point in $points) {
        $screenPoint = New-Object Phase23Probe+Point
        $screenPoint.X = $windowRect.Left + $point.X; $screenPoint.Y = $windowRect.Top + $point.Y
        $inRegion = [Phase23Probe]::PtInRegion($region, $point.X, $point.Y)
        $pointOwner = Get-Owner ([Phase23Probe]::WindowFromPoint($screenPoint))
        $expectedOwner = $(if ($point.ExpectInside) { $testProcess.Id } else { $notepad.ProcessId })
        $routing += [ordered]@{ Name = $point.Name; X = $screenPoint.X; Y = $screenPoint.Y; InsideRegion = $inRegion; OwnerProcessId = $pointOwner; ExpectedOwner = $expectedOwner; Pass = ($inRegion -eq $point.ExpectInside -and $pointOwner -eq $expectedOwner) }
    }
    $result.Geometry = [ordered]@{ Window = (Convert-Rect $windowRect); PrimaryOuterBounds = (Convert-Rect $displayDuring.Bounds); RegionBox = (Convert-Rect $regionBox); RegionType = $regionType; CenterOffsetPixels = (($windowRect.Left + $windowRect.Right - $displayDuring.Bounds.Left - $displayDuring.Bounds.Right) / 2.0); TopOffsetPixels = ($windowRect.Top - $displayDuring.Bounds.Top) }
    $result.Routing = $routing
    $result.Criteria.CenteredCompactTopBounds = $(if ($width -le 640 -and $height -le 240 -and $result.Geometry.CenterOffsetPixels -eq 0 -and $result.Geometry.TopOffsetPixels -eq 0) { 'PASS' } else { 'FAIL' })
    $result.Criteria.NativeRoundedOutline = $(if ($regionType -eq 3 -and @($routing | Where-Object { $_.Name -like '*corner*' -and -not $_.Pass }).Count -eq 0) { 'PASS' } else { 'FAIL' })
    $result.Criteria.ExteriorPointRouting = $(if (@($routing | Where-Object { -not $_.Pass }).Count -eq 0) { 'PASS' } else { 'FAIL' })
    Write-Host ('Measured window and region: ' + ($result.Geometry | ConvertTo-Json -Depth 4 -Compress))
    Write-Host 'Click the dark visible island once, away from the Close button.'
    $result.InsideClick = Wait-ForClick $true
    Write-Host 'Now click the white Notepad document immediately beside or below the island. Do not click the island again.'
    $result.OutsideClick = Wait-ForClick $false
    $result.Criteria.VisibleIslandInputRouting = 'PASS'
    $result.Criteria.ExteriorClickAndFocus = $(if ($result.OutsideClick.ForegroundAfter.ProcessId -eq $notepad.ProcessId) { 'PASS' } else { 'FAIL' })
    Write-Host "Click 'Close test window' now."
    if (-not $testProcess.WaitForExit(600000)) { throw 'The prototype did not close within ten minutes.' }
    $testProcess.WaitForExit()
    $result.Exit = [ordered]@{ ProcessId = $testProcess.Id; ExitCode = $testProcess.ExitCode; RemainingProcesses = @(Get-Process -Name IslandTaskbar -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id) }
    $result.Criteria.CleanShutdown = $(if ($result.Exit.ExitCode -eq 0 -and $result.Exit.RemainingProcesses.Count -eq 0) { 'PASS' } else { 'FAIL' })
    $displayAfter = [Phase23Probe]::Primary()
    $result.WorkArea = [ordered]@{ Before = (Convert-Rect $displayBefore.Work); During = (Convert-Rect $displayDuring.Work); After = (Convert-Rect $displayAfter.Work) }
    $result.Criteria.WorkAreaUnchanged = $(if (($result.WorkArea.Before | ConvertTo-Json -Compress) -eq ($result.WorkArea.During | ConvertTo-Json -Compress) -and ($result.WorkArea.Before | ConvertTo-Json -Compress) -eq ($result.WorkArea.After | ConvertTo-Json -Compress)) { 'PASS' } else { 'FAIL' })
    $version = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    $result.Environment = [ordered]@{ WindowsBuild = ($version.CurrentBuild + '.' + $version.UBR); Architecture = 'x64'; UnderlyingApplication = 'Notepad'; UnderlyingProcessId = $notepad.ProcessId }
    $testProcess.Dispose()
} catch {
    $result.Error = $_.Exception.Message
    Write-Host ('Verification error: ' + $result.Error) -ForegroundColor Red
} finally {
    if ($region -ne [IntPtr]::Zero) { [Phase23Probe]::DeleteObject($region) | Out-Null }
    if ($oldDpiContext -ne [IntPtr]::Zero) { [Phase23Probe]::SetThreadDpiAwarenessContext($oldDpiContext) | Out-Null }
    foreach ($criterion in @('CenteredCompactTopBounds','NativeRoundedOutline','ExteriorPointRouting','VisibleIslandInputRouting','ExteriorClickAndFocus','CleanShutdown','WorkAreaUnchanged')) {
        if (-not $result.Criteria.Contains($criterion)) { $result.Criteria[$criterion] = 'NOT VERIFIED' }
    }
    $result | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $resultPath -Encoding UTF8
    Write-Host ($result.Criteria | ConvertTo-Json)
    Write-Host ('Results saved to: ' + $resultPath)
    Write-Host 'Also report whether you saw Notepad through the margins/corners and whether the island-click counter increased.'
    Read-Host 'Press Enter to close this verification console' | Out-Null
}
