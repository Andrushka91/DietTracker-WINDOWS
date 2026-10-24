# Live verification of the PENDING / DONE / MISSED business rules.
param([string]$Out = 'D:\PROGRAMMING\WINDOWS_APPS')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class W32 {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

$p = Get-Process DietTracker | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { throw 'no window' }
$main = $p.MainWindowHandle
[W32]::SetForegroundWindow($main) | Out-Null

function Shot([IntPtr]$hwnd, [string]$path) {
  $r = New-Object W32+RECT
  [W32]::GetWindowRect($hwnd, [ref]$r) | Out-Null
  $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
  $bmp = New-Object System.Drawing.Bitmap($w, $h)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc()
  [W32]::PrintWindow($hwnd, $hdc, 2) | Out-Null
  $g.ReleaseHdc($hdc); $g.Dispose()
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  Write-Host "  saved $path"
}

function Find-Named($root, [string]$pattern) {
  $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
  foreach ($e in $all) { if ($e.Current.Name -match $pattern) { return $e } }
  return $null
}

function Invoke-Element($root, [string]$pattern, [string]$label) {
  $el = Find-Named $root $pattern
  if ($el -eq $null) { Write-Host "NOT FOUND: $label"; return $false }
  try {
    $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Write-Host "invoked: $label"
    return $true
  } catch { Write-Host "BLOCKED (disabled?): $label"; return $false }
}

$root = [System.Windows.Automation.AutomationElement]::FromHandle($main)
Start-Sleep -Seconds 5

# 1. Today: state buttons must be enabled (today is editable) - visible in the screenshot.
Shot $main "$Out\_r5_today.png"

# 2. Past day (previous): X on a DONE meal must turn it MISSED (red).
[void](Invoke-Element $root '^Previous$' 'Previous day')
Start-Sleep -Seconds 3
Shot $main "$Out\_r5_past_before.png"
Invoke-Element $root '^Mark as MISSED$' 'past card X (expect enabled)'
Start-Sleep -Seconds 3
Shot $main "$Out\_r5_past_missed.png"

# 3. Back to done so the day is left as it was.
Invoke-Element $root '^Mark as DONE$' 'past card check (restore to done)'
Start-Sleep -Seconds 2

# 4. Future day (two days ahead): everything must be locked.
[void](Invoke-Element $root '^Next$' 'Next (to today)')
Start-Sleep -Seconds 2
[void](Invoke-Element $root '^Next$' 'Next (to tomorrow)')
Start-Sleep -Seconds 3
Invoke-Element $root '^Mark as MISSED$' 'future card X (expect BLOCKED)'
Invoke-Element $root '^Mark as DONE$' 'future card check (expect BLOCKED)'
Invoke-Element $root '^Mark all meals as MISSED$' 'future day X (expect BLOCKED)'
Shot $main "$Out\_r5_future.png"

# 5. Leave the app on today.
[void](Invoke-Element $root '^Today$' 'Today')
Start-Sleep -Seconds 2
Write-Host 'done'