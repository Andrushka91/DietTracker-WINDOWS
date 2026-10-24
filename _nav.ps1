param([int]$Clicks = 1, [string]$Name = 'Previous')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$proc = Get-Process DietTracker -ErrorAction Stop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$window = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
$condition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, $Name)
$button = $window.FindFirst([System.Windows.Automation.TreeScope]::Subtree, $condition)
if ($null -eq $button) { throw "$Name button not found" }

# Buttons expose InvokePattern; navigation items expose SelectionItemPattern.
$action = $null
if ($button.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$action)) {
    for ($i = 0; $i -lt $Clicks; $i++) { $action.Invoke(); Start-Sleep -Milliseconds 700 }
    Write-Output ("invoked {0} via Invoke" -f $Name)
    exit 0
}
if ($button.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$action)) {
    $action.Select()
    Write-Output ("invoked {0} via Select" -f $Name)
    exit 0
}
if ($button.TryGetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern, [ref]$action)) {
    $action.ScrollIntoView()
    Write-Output ("invoked {0} via ScrollIntoView" -f $Name)
    exit 0
}
throw "no supported pattern for $Name"