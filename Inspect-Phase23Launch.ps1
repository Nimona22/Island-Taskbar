# Read-only launch-context diagnostics. Does not launch or change any window or settings.
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class IslandLaunchContextProbe
{
    [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] public static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool answer);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool QueryInformationJobObject(IntPtr job, int type, out uint info, uint size, out uint returned);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool GetTokenInformation(IntPtr token, int type, IntPtr buffer, uint size, out uint returned);
    [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    public static int IntegrityRid()
    {
        IntPtr token;
        if (!OpenProcessToken(GetCurrentProcess(), 8, out token)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        IntPtr buffer = IntPtr.Zero;
        try {
            uint size;
            GetTokenInformation(token, 25, IntPtr.Zero, 0, out size);
            buffer = Marshal.AllocHGlobal((int)size);
            if (!GetTokenInformation(token, 25, buffer, size, out size)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            IntPtr sid = Marshal.ReadIntPtr(buffer);
            byte count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            return Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
        } finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); CloseHandle(token); }
    }
}
'@
$inJob = $false
[IslandLaunchContextProbe]::IsProcessInJob([IslandLaunchContextProbe]::GetCurrentProcess(), [IntPtr]::Zero, [ref]$inJob) | Out-Null
[uint32]$uiFlags = 0
[uint32]$returned = 0
$jobQuery = [IslandLaunchContextProbe]::QueryInformationJobObject([IntPtr]::Zero, 4, [ref]$uiFlags, 4, [ref]$returned)
$jobError = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
$self = Get-CimInstance Win32_Process -Filter ('ProcessId=' + $PID)
$parent = Get-CimInstance Win32_Process -Filter ('ProcessId=' + $self.ParentProcessId)
$result = [ordered]@{
    Date = (Get-Date).ToString('o')
    ProcessId = $PID
    ParentProcessId = $self.ParentProcessId
    ParentName = $parent.Name
    SessionId = $self.SessionId
    PowerShellVersion = $PSVersionTable.PSVersion.ToString()
    Is64Bit = [Environment]::Is64BitProcess
    InJob = $inJob
    JobQuerySucceeded = $jobQuery
    JobUIFlags = $uiFlags
    JobQueryError = $(if ($jobQuery) { 0 } else { $jobError })
    IntegrityRid = [IslandLaunchContextProbe]::IntegrityRid()
    CompatibilityLayer = [Environment]::GetEnvironmentVariable('__COMPAT_LAYER')
}
$resultPath = Join-Path $PSScriptRoot 'Phase23.LaunchContext.json'
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $resultPath -Encoding UTF8
$result | ConvertTo-Json -Depth 4
Write-Host ('Diagnostics saved to: ' + $resultPath)
