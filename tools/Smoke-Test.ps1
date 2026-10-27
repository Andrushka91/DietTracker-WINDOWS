<#
.SYNOPSIS
    Smoke tests the Diet Tracker desktop application and measures cold start time and idle memory.

.DESCRIPTION
    Starts the built (unpackaged) executable, waits for its main window, records how long that took,
    samples the working set and private memory, then closes the process. The script is used by the
    automated UI smoke test and by the performance measurements documented in
    docs/performance/measurements.md.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Smoke-Test.ps1
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [int] $StartupTimeoutSeconds = 60,
    [int] $SampleSeconds = 5
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class WindowProbe
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    public static bool HasVisibleWindow(uint processId)
    {
        bool found = false;
        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
        {
            uint owner;
            GetWindowThreadProcessId(hWnd, out owner);
            if (owner == processId && IsWindowVisible(hWnd))
            {
                found = true;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
"@

$repositoryRoot = Split-Path -Parent $PSScriptRoot
# The optimized application lives in dist/exec ("exec" = executable); dist/debug is the debuggable build.
$distFolder = if ($Configuration -eq 'Debug') { 'debug' } else { 'exec' }
$outputDirectory = Join-Path $repositoryRoot "dist\$distFolder"
$executable = Join-Path $outputDirectory 'DietTracker.exe'

if (-not (Test-Path $executable)) {
    throw "The application has not been built yet: $executable"
}

Write-Host "Starting $executable"

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$process = Start-Process -FilePath $executable -WorkingDirectory $outputDirectory -PassThru

$windowReady = $false
while ($stopwatch.Elapsed.TotalSeconds -lt $StartupTimeoutSeconds) {
    Start-Sleep -Milliseconds 100
    $process.Refresh()

    if ($process.HasExited) {
        throw "Diet Tracker exited during startup with code $($process.ExitCode)."
    }

    # WinUI 3 hosts its window on a framework thread, so the window is located through the Win32 API.
    if ([WindowProbe]::HasVisibleWindow([uint32]$process.Id)) {
        $windowReady = $true
        break
    }
}

$startupSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 2)
$stopwatch.Stop()

if (-not $windowReady) {
    $process.Kill()
    throw "No visible window appeared within $StartupTimeoutSeconds seconds."
}

Start-Sleep -Seconds $SampleSeconds
$process.Refresh()

$result = [pscustomobject]@{
    StartupSeconds      = $startupSeconds
    WorkingSetMB        = [Math]::Round($process.WorkingSet64 / 1MB, 1)
    PrivateMemoryMB     = [Math]::Round($process.PrivateMemorySize64 / 1MB, 1)
    Threads             = $process.Threads.Count
    Handles             = $process.HandleCount
    Responding          = $process.Responding
    MainWindowTitle     = $process.MainWindowTitle
}

$process.CloseMainWindow() | Out-Null
Start-Sleep -Seconds 2

if (-not $process.HasExited) {
    $process.Kill()
}

$result | Format-List
