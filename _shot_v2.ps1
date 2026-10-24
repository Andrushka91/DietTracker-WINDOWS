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
  Write-Host "  saved $path ($w x $h)"
}

function Find-Named($root, [string]$pattern) {
  $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
  foreach ($e in $all) { if ($e.Current.Name -match $pattern) { return $e } }
  return $null
}

function Click($root, [string]$pattern, [string]$label) {
  $el = Find-Named $root $pattern
  if ($el -eq $null) { Write-Host "NOT FOUND: $label"; return $false }
  try { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
  catch { try { $el.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() } catch { Write-Host "cannot invoke: $label"; return $false } }
  Write-Host "clicked: $label"
  return $true
}

$root = [System.Windows.Automation.AutomationElement]::FromHandle($main)
Start-Sleep -Seconds 6

# 0. Fresh dashboard right after start-up (no clicks at all)
Shot $main "$Out\_v2_fresh.png"

# 1. Dashboard (today): squares + counters above the cards
[void](Click $root '^Dashboard$|Panou' 'Dashboard nav')
Start-Sleep -Seconds 5
Shot $main "$Out\_v2_dashboard.png"

# 2. Previous day -> pending / missed cards with the bigger icons
[void](Click $root '^Previous$|Previous day|Ziua anterioar' 'Previous day')
Start-Sleep -Seconds 4
Shot $main "$Out\_v2_cards_prev.png"

# 3. Calendar page
[void](Click $root '^Calendar$|Calendar' 'Calendar nav')
Start-Sleep -Seconds 4
Shot $main "$Out\_v2_calendar.png"

# 4. Statistics page
[void](Click $root 'Statistics|Statistici' 'Statistics nav')
Start-Sleep -Seconds 4
Shot $main "$Out\_v2_stats.png"