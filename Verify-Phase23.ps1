# Phase 2.3 verification only: queries window regions/input routing; does not inject input or alter Windows settings.
$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot 'src\IslandTaskbar\bin\x64\Debug\net10.0-windows10.0.26100.0\IslandTaskbar.exe'
$resultPath = Join-Path $PSScriptRoot 'Phase23.RuntimeResults.json'
$result = [ordered]@{ Phase = '2.3'; Date = (Get-Date).ToString('o'); Criteria = [ordered]@{} }
$region = [IntPtr]::Zero
$oldDpiContext = [IntPtr]::Zero
Write-Host 'Phase 2.3 verifier revision 4: native process-handle shutdown check.'
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
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback, IntPtr data);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("user32.dll")] public static extern int GetWindowRgn(IntPtr window, IntPtr region);
    [DllImport("gdi32.dll")] public static extern int GetRgnBox(IntPtr region, out Rect rect);
    [DllImport("gdi32.dll")] public static extern bool PtInRegion(IntPtr region, int x, int y);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint thread, WindowCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowTextW(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr window, StringBuilder text, int capacity);
    public static string ClassName(IntPtr window) { StringBuilder text = new StringBuilder(256); GetClassNameW(window, text, text.Capacity); return text.ToString(); }
    public static IntPtr[] ProcessWindows(uint process)
    {
        System.Collections.Generic.List<IntPtr> found = new System.Collections.Generic.List<IntPtr>();
        WindowCallback callback = delegate(IntPtr handle, IntPtr data) { uint owner; GetWindowThreadProcessId(handle, out owner); if (owner == process) found.Add(handle); return true; };
        EnumWindows(callback, IntPtr.Zero); return found.ToArray();
    }
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
                    $withinWindow = $cursor.X -ge $windowRect.Left -and $cursor.X -lt $windowRect.Right -and $cursor.Y -ge $windowRect.Top -and $cursor.Y -lt $windowRect.Bottom
                    $hitOwner = Get-Owner ([Phase23Probe]::WindowFromPoint($cursor))
                    if (($inside -and $inIsland -and $hitOwner -eq $testProcess.Id) -or (-not $inside -and $withinWindow -and -not $inIsland -and $hitOwner -eq $notepad.ProcessId)) {
                        while (([Phase23Probe]::GetAsyncKeyState(1) -band 0x8000) -ne 0) { Start-Sleep -Milliseconds 20 }
                        Start-Sleep -Milliseconds 250
                        return [ordered]@{ X = $cursor.X; Y = $cursor.Y; WithinWindowBounds = $withinWindow; InsideRegion = $inIsland; PointOwnerProcessId = $hitOwner; ForegroundAfter = (Get-Foreground) }
                    }
                    if (-not $inside -and -not $withinWindow) {
                        Write-Host 'That click was beyond the overlay bounds. Click the thin Notepad strip directly below the dark island, close to its curved edge.'
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
    Write-Host 'The Phase 2.3 window will launch automatically. Leave mouse and keyboard untouched for 30 seconds after it appears. Do not return to PowerShell during that wait.'
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
    $testProcessHandle = $testProcess.Handle
    $result.Artifact = [ordered]@{ Executable = $executable; AssemblySHA256 = (Get-FileHash -LiteralPath (Join-Path ([IO.Path]::GetDirectoryName($executable)) 'IslandTaskbar.dll') -Algorithm SHA256).Hash }
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
    # A native region can exist before the nonactivating window has appeared. Retain both observations.
    $initialPoint = New-Object Phase23Probe+Point
    $initialRect = New-Object Phase23Probe+Rect
    if (-not [Phase23Probe]::GetWindowRect($windowHandle, [ref]$initialRect)) { throw 'Initial GetWindowRect failed.' }
    $initialPoint.X = $initialRect.Left + [int](($regionBox.Left + $regionBox.Right) / 2)
    $initialPoint.Y = $initialRect.Top + [int](($regionBox.Top + $regionBox.Bottom) / 2)
    $result.InitialRoutingObservation = [ordered]@{ WindowVisible = [Phase23Probe]::IsWindowVisible($windowHandle); InsidePointOwner = (Get-Owner ([Phase23Probe]::WindowFromPoint($initialPoint))); ExpectedOwner = $testProcess.Id }
    $focusChanges = @()
    $settleDeadline = [DateTime]::UtcNow.AddSeconds(10)
    while ([DateTime]::UtcNow -lt $settleDeadline) {
        if ($testProcess.HasExited) { throw 'The prototype exited during the startup observation.' }
        $foreground = Get-Foreground
        if ($foreground.Handle -ne $notepad.Handle) {
            if ($focusChanges.Count -eq 0 -or $focusChanges[-1].Handle -ne $foreground.Handle) { $focusChanges += $foreground }
        }
        Start-Sleep -Milliseconds 100
    }
    $result.StartupFocus = [ordered]@{ Before = $notepad; After = (Get-Foreground); ChangesObserved = $focusChanges; ObservationSeconds = 10; PollIntervalMilliseconds = 100 }
    $result.Criteria.StartupFocusPreserved = $(if ($focusChanges.Count -eq 0 -and $result.StartupFocus.After.Handle -eq $notepad.Handle) { 'PASS' } else { 'FAIL' })
    if (-not [Phase23Probe]::IsWindowVisible($windowHandle)) { throw 'The island window was not visible after the startup observation.' }
    $regionType = [Phase23Probe]::GetWindowRgn($windowHandle, $region)
    if ($regionType -le 1 -or [Phase23Probe]::GetRgnBox($region, [ref]$regionBox) -le 1) { throw 'The visible island region disappeared.' }
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
    $result.Criteria.ExteriorPointRouting = $(if (@($routing | Where-Object { $_.Name -ne 'Inside' -and -not $_.Pass }).Count -eq 0) { 'PASS' } else { 'FAIL' })
    $result.Criteria.InteriorPointRouting = $(if ($routing[0].Pass) { 'PASS' } else { 'FAIL' })
    $rootHandle = [Phase23Probe]::GetAncestor($windowHandle, 2)
    $result.WindowIdentity = [ordered]@{ MeasuredHandle = $windowHandle.ToInt64(); RootHandle = $rootHandle.ToInt64(); MeasuredClass = [Phase23Probe]::ClassName($windowHandle); RootClass = [Phase23Probe]::ClassName($rootHandle); MeasuredStyle = [Phase23Probe]::GetWindowLongPtr($windowHandle, -16).ToInt64(); RootExtendedStyle = [Phase23Probe]::GetWindowLongPtr($rootHandle, -20).ToInt64() }
    $result.ProcessWindows = @([Phase23Probe]::ProcessWindows([uint32]$testProcess.Id) | ForEach-Object { [ordered]@{ Handle = $_.ToInt64(); Class = [Phase23Probe]::ClassName($_); Visible = [Phase23Probe]::IsWindowVisible($_); ExtendedStyle = [Phase23Probe]::GetWindowLongPtr($_, -20).ToInt64() } })
    $result.Topmost = [ordered]@{ ExtendedStyle = [Phase23Probe]::GetWindowLongPtr($rootHandle, -20).ToInt64(); Foreground = (Get-Foreground); InsidePointOwner = $routing[0].OwnerProcessId }
    $result.Criteria.TopmostAboveNotepad = $(if (($result.Topmost.ExtendedStyle -band 8) -ne 0 -and $routing[0].Pass -and $result.Topmost.Foreground.Handle -eq $notepad.Handle) { 'PASS' } else { 'FAIL' })
    Write-Host ('Measured window and region: ' + ($result.Geometry | ConvertTo-Json -Depth 4 -Compress))
    Write-Host 'Click the diagnostic heading in the dark island once. Check that the island-click counter increases. Do not click Close yet.'
    $result.InsideClick = Wait-ForClick $true
    Write-Host ('Now click the thin Notepad strip DIRECTLY below the dark island, near screen point (' + ($windowRect.Left + $centerX) + ', ' + [int](($windowRect.Top + $regionBox.Bottom + $windowRect.Bottom) / 2) + '). A click far down the document does not count.')
    $result.OutsideClick = Wait-ForClick $false
    $result.Criteria.VisibleIslandInputRouting = 'PASS'
    $result.Criteria.ExteriorClickAndFocus = $(if ($result.OutsideClick.ForegroundAfter.ProcessId -eq $notepad.ProcessId) { 'PASS' } else { 'FAIL' })
    $postClickPoint = New-Object Phase23Probe+Point
    $postClickPoint.X = $windowRect.Left + $centerX; $postClickPoint.Y = $windowRect.Top + [int](($regionBox.Top + $regionBox.Bottom) / 2)
    $result.TopmostAfterExteriorClick = [ordered]@{ ExtendedStyle = [Phase23Probe]::GetWindowLongPtr($rootHandle, -20).ToInt64(); Foreground = (Get-Foreground); InsidePointOwner = (Get-Owner ([Phase23Probe]::WindowFromPoint($postClickPoint))) }
    $result.Criteria.TopmostAboveNotepad = $(if ($result.Criteria.TopmostAboveNotepad -eq 'PASS' -and ($result.TopmostAfterExteriorClick.ExtendedStyle -band 8) -ne 0 -and $result.TopmostAfterExteriorClick.InsidePointOwner -eq $testProcess.Id -and $result.TopmostAfterExteriorClick.Foreground.Handle -eq $notepad.Handle) { 'PASS' } else { 'FAIL' })
    Write-Host 'Before closing: check that the island still overlaps active Notepad, the lower corners are rounded, and Notepad shows through the exterior. Check that the outside click did not increase the island counter.'
    Write-Host "Click 'Close test window' now."
    $closeDeadline = [DateTime]::UtcNow.AddMinutes(10)
    do { $exitWait = [Phase23Probe]::WaitForSingleObject($testProcessHandle, 100) } while ($exitWait -eq 258 -and [DateTime]::UtcNow -lt $closeDeadline)
    if ($exitWait -eq 258) { throw 'The prototype did not close within ten minutes.' }
    if ($exitWait -ne 0) { throw ('Native process wait failed with status ' + $exitWait) }
    Write-Host 'Process exit observed. Reading native exit code.'
    [uint32]$exitCode = 0
    if (-not [Phase23Probe]::GetExitCodeProcess($testProcessHandle, [ref]$exitCode)) { throw 'Could not read the completed process exit code.' }
    $result.Exit = [ordered]@{ ProcessId = $testProcess.Id; ExitCode = $exitCode; RemainingProcesses = @(Get-Process -Name IslandTaskbar -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id) }
    $tracePath = Join-Path ([IO.Path]::GetDirectoryName($executable)) 'Phase23.WindowTrace.txt'
    if (Test-Path -LiteralPath $tracePath) { $result.WindowTrace = @(Get-Content -LiteralPath $tracePath | Where-Object { $_ -match ('PID=' + $testProcess.Id + ' ') }) }
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
    foreach ($criterion in @('CenteredCompactTopBounds','NativeRoundedOutline','ExteriorPointRouting','InteriorPointRouting','StartupFocusPreserved','TopmostAboveNotepad','VisibleIslandInputRouting','ExteriorClickAndFocus','CleanShutdown','WorkAreaUnchanged')) {
        if (-not $result.Criteria.Contains($criterion)) { $result.Criteria[$criterion] = 'NOT VERIFIED' }
    }
    Write-Host 'Saving verification results.'
    $result | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $resultPath -Encoding UTF8
    Write-Host ($result.Criteria | ConvertTo-Json)
    Write-Host ('Results saved to: ' + $resultPath)
    Write-Host 'Also report whether you saw Notepad through the margins/corners and whether the island-click counter increased.'
    Read-Host 'Press Enter to close this verification console' | Out-Null
}
