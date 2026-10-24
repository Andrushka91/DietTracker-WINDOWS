param([string]$Out = 'D:\PROGRAMMING\WINDOWS_APPS\_shot.png')
$ErrorActionPreference = 'Stop'
$p = Get-Process DietTracker -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) {
  Start-Process 'D:\PROGRAMMING\WINDOWS_APPS\dist\debug\DietTracker.exe'
  for ($i = 0; $i -lt 40 -and -not $p; $i++) {
    Start-Sleep -Milliseconds 1000
    $p = Get-Process DietTracker -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
  }
}
if (-not $p) { throw 'no window' }
Write-Output ("window title: " + $p.MainWindowTitle)
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Shot32 {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
'@
# Per-monitor-v2 DPI awareness, so GetWindowRect returns PHYSICAL pixels and the capture bitmap
# covers the whole window (the previous DPI-unaware capture clipped the right card column).
$oldContext = [Shot32]::SetThreadDpiAwarenessContext([IntPtr](-4))
# Grow the window so the whole card grid is visible; the size limiter clamps to the allowed maximum.
# SWP_NOZORDER | SWP_NOACTIVATE (0x0004 | 0x0010) - the window IS resized here (the old flags
# 0x0001|0x0002 meant NOSIZE|NOMOVE and never changed the size at all).
[Shot32]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, 0, 0, 1900, 1750, 0x0004 -bor 0x0010) | Out-Null
Start-Sleep -Milliseconds 2500
$r = New-Object Shot32+RECT
for ($i = 0; $i -lt 20; $i++) {
  [Shot32]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
  if (($r.Right - $r.Left) -gt 200 -and ($r.Bottom - $r.Top) -gt 200) { break }
  Start-Sleep -Milliseconds 500
}
$w = $r.Right - $r.Left
$h = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [Shot32]::PrintWindow($p.MainWindowHandle, $hdc, 2)
$g.ReleaseHdc($hdc)
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output ("PrintWindow: " + $ok + " saved " + $w + "x" + $h)