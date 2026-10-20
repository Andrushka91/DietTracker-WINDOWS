param(
    [string]$ExePath = "D:\PROGRAMMING\WINDOWS_APPS\dist\exec\DietTracker.exe",
    [string]$OutDir = "$env:TEMP\diettracker-ui",
    [string]$Target = "Statist",
    [string]$ShotPrefix = "page",
    [switch]$ResizeSmall,
    [switch]$ResizeLarge,
    [switch]$Maximize
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase, System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
[Win32]::SetProcessDPIAware() | Out-Null

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Screenshot([IntPtr]$hwnd, [string]$path) {
    $rect = New-Object Win32+RECT
    [Win32]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return }
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [Win32]::PrintWindow($hwnd, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  screenshot -> $path ($w x $h)"
}

if (-not (Test-Path $ExePath)) { throw "Executable not found: $ExePath" }

Write-Host "Starting $ExePath ..."
$proc = Start-Process $ExePath -PassThru

$main = [IntPtr]::Zero
for ($i = 0; $i -lt 100; $i++) {
    $proc.Refresh()
    if ($proc.MainWindowHandle -ne [IntPtr]::Zero) { $main = $proc.MainWindowHandle; break }
    Start-Sleep -Milliseconds 300
}
if ($main -eq [IntPtr]::Zero) { throw "Main window did not appear." }
Write-Host "Main window: $main"

Start-Sleep -Seconds 4
[Win32]::SetForegroundWindow($main) | Out-Null
Screenshot $main "$OutDir\01-dashboard.png"

$root = [System.Windows.Automation.AutomationElement]::FromHandle($main)

function Find-NamedElement($root, [string]$pattern) {
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($e in $all) {
        if ($e.Current.Name -match $pattern) { return $e }
    }
    return $null
}

function Activate-Element($el, [string]$what) {
    $invoked = $false
    try { $p = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern); $p.Invoke(); $invoked = $true } catch {}
    if (-not $invoked) {
        try { $p = $el.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern); $p.Select(); $invoked = $true } catch {}
    }
    Write-Host "Activated '$what' via UIA: $invoked"
}

$stats = Find-NamedElement $root $Target
if ($stats -eq $null) {
    Write-Host "WARNING: '$Target' nav element not found; dumping element names:"
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($e in $all) { if ($e.Current.Name) { Write-Host ("  [" + $e.Current.ControlType.ProgrammaticName + "] " + $e.Current.Name) } }
    throw "Target element not found"
}
Write-Host "Found target element: '$($stats.Current.Name)'"
Activate-Element $stats $Target

Start-Sleep -Seconds 6
$proc.Refresh()
$survived = -not $proc.HasExited
Write-Host "Process alive after opening $Target : $survived"

if ($ResizeSmall -or $ResizeLarge) {
    if ($ResizeSmall) {
        [Win32]::SetWindowPos($main, [IntPtr]::Zero, 100, 100, 900, 700, 0x40) | Out-Null  # SWP_NOZORDER
        Start-Sleep -Seconds 3
        $r = New-Object Win32+RECT
        [Win32]::GetWindowRect($main, [ref]$r) | Out-Null
        Write-Host "  rect after small resize: $($r.Left),$($r.Top) $($r.Right - $r.Left)x$($r.Bottom - $r.Top)"
        Screenshot $main "$OutDir\02-statistics-small.png"
    }
    if ($ResizeLarge) {
        [Win32]::SetWindowPos($main, [IntPtr]::Zero, 50, 50, 1600, 1000, 0x40) | Out-Null
        Start-Sleep -Seconds 3
        Screenshot $main "$OutDir\02-statistics-large.png"
    }
    $proc.Refresh()
    Write-Host "Process alive after resize: $(-not $proc.HasExited)"
}

if ($Maximize) {
    # SC_MAXIMIZE: the window must clamp to the content-driven maximum size (no empty black space).
    [Win32]::PostMessage($main, 0x0112, [IntPtr]0xF030, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Seconds 3
    $r = New-Object Win32+RECT
    [Win32]::GetWindowRect($main, [ref]$r) | Out-Null
    Write-Host "  rect after maximize: $($r.Left),$($r.Top) $($r.Right - $r.Left)x$($r.Bottom - $r.Top)"
    Screenshot $main "$OutDir\05-maximized.png"
    $proc.Refresh()
    Write-Host "Process alive after maximize: $(-not $proc.HasExited)"
}

Start-Sleep -Seconds 1
$proc.Refresh()
if (-not $proc.HasExited) {
    Screenshot $main "$OutDir\02-after-statistics.png"
    Activate-Element (Find-NamedElement $root 'Dashboard|Ziua de azi|Today') "dashboard"
    Start-Sleep -Seconds 4
    $proc.Refresh()
    Write-Host "Process alive after returning to dashboard: $(-not $proc.HasExited)"
    if (-not $proc.HasExited) {
        [Win32]::SetWindowPos($main, [IntPtr]::Zero, 100, 100, 900, 700, 0x40) | Out-Null
        Start-Sleep -Seconds 3
        Screenshot $main "$OutDir\03-dashboard-small.png"
        [Win32]::SetWindowPos($main, [IntPtr]::Zero, 50, 50, 1600, 1000, 0x40) | Out-Null
        Start-Sleep -Seconds 3
        Screenshot $main "$OutDir\04-dashboard-large.png"
    }
}

$proc.Refresh()
Write-Host "FINAL: process exited = $($proc.HasExited)"
if (-not $proc.HasExited) { $proc.Kill() }
