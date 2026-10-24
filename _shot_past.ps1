param([string]$Out = 'D:\PROGRAMMING\WINDOWS_APPS')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class W32c {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
$p = Get-Process DietTracker | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$main = $p.MainWindowHandle
[W32c]::SetForegroundWindow($main) | Out-Null
$root = [System.Windows.Automation.AutomationElement]::FromHandle($main)
function Click($r, $pat) {
  $all = $r.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
  foreach ($e in $all) { if ($e.Current.Name -match $pat) { try { $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); return $true } catch {} } }
  return $false
}
Start-Sleep -Seconds 2
[void](Click $root '^Today$')
Start-Sleep -Seconds 2
[void](Click $root '^Previous$')
Start-Sleep -Seconds 3
$r = New-Object W32c+RECT
[W32c]::GetWindowRect($main, [ref]$r) | Out-Null
$w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[W32c]::PrintWindow($main, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc); $g.Dispose()
$bmp.Save("$Out\_r5_past20.png", [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host 'saved past20'
[void](Click $root '^Today$')
Write-Host 'back to today'
