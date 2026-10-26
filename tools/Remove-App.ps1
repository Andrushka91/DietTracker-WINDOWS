<#
.SYNOPSIS
    Uninstalls the DietTracker MSIX package as fast as Windows allows.

.DESCRIPTION
    Remove-AppxPackage spends most of its time in two places:

    1. Waiting for the running application to shut down. A packaged WinUI app gets a graceful
       shutdown signal first and only then releases its files, so the deployment engine idles
       until the process exits. This script kills the process up front, which is the single
       biggest saving (a Settings-based uninstall of a running app took ~31 s; the same removal
       without a running process takes under a second).

    2. Deleting the package payload from C:\Program Files\WindowsApps. That cost scales with the
       package size - which is why the publish profiles in
       src/DietTracker.App/Properties/PublishProfiles exist: they keep foreign-architecture and
       foreign-OS native assets out of the package (81.7 MB -> 31.0 MB here).

    The script reports how long the removal actually took, so regressions are visible.

.PARAMETER KeepData
    Saves the packaged build's data before the removal: Windows deletes the package's private
    LocalState folder together with the package, so the script copies it to
    %LOCALAPPDATA%\DietTracker\LocalState-backup first. Without this switch the packaged data is
    removed by Windows and the unpackaged build's %LOCALAPPDATA%\DietTracker is deleted too, so
    the next start begins from scratch.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Remove-App.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Remove-App.ps1 -KeepData
#>
[CmdletBinding()]
param(
    # Keeps the application data instead of letting it go away with the package.
    [switch] $KeepData
)

$ErrorActionPreference = 'Stop'

$unpackagedData = Join-Path $env:LOCALAPPDATA 'DietTracker'
# Packaged layout: %LOCALAPPDATA%\Packages\<package family name>\LocalState (README "Where the data
# lives"). It is removed by Remove-AppxPackage, so it has to be rescued before the removal.
$packageDataRoot = Join-Path $env:LOCALAPPDATA 'Packages'
$localState = Get-ChildItem -Path $packageDataRoot -Directory -Filter 'DietTracker.Desktop_*' -ErrorAction SilentlyContinue |
    ForEach-Object { Join-Path $_.FullName 'LocalState' } |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1

# 1. Save data if the caller wants to keep it (must happen before the package disappears).
$backup = Join-Path $unpackagedData 'LocalState-backup'
if ($KeepData -and $localState) {
    if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force }
    Copy-Item -LiteralPath $localState -Destination $backup -Recurse -Force
    Write-Host "Packaged data backed up to $backup"
} elseif ($KeepData) {
    Write-Host 'No packaged LocalState found - nothing to back up.'
}

# 2. Close a running instance: the deployment engine would otherwise wait for the graceful
#    shutdown handshake before it even starts removing the package. This is the single biggest
#    saving - the removal of a running app waits for that timeout, an idle removal does not.
$running = Get-Process -Name 'DietTracker' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Stopping $($running.Count) running DietTracker instance(s)..."
    $running | Stop-Process -Force
    $deadline = (Get-Date).AddSeconds(5)
    while ((Get-Process -Name 'DietTracker' -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 200
    }
}

# 3. Remove the package and time it.
$package = Get-AppxPackage -Name 'DietTracker*' -ErrorAction SilentlyContinue
if (-not $package) {
    Write-Host 'DietTracker is not installed - nothing to remove.'
} else {
    Write-Host "Removing $($package.PackageFullName)..."
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    Remove-AppxPackage -Package $package.PackageFullName
    $stopwatch.Stop()
    Write-Host ("Removed in {0:N2}s." -f $stopwatch.Elapsed.TotalSeconds)
}

# 4. Unpackaged data (the packaged one was already deleted with the package by Windows).
if (-not $KeepData -and (Test-Path -LiteralPath $unpackagedData)) {
    Remove-Item -LiteralPath $unpackagedData -Recurse -Force
    Write-Host "Application data deleted: $unpackagedData"
} elseif ($KeepData) {
    Write-Host "Unpackaged application data kept at $unpackagedData"
}

# 5. Verify the package is really gone (a leftover would show as Installed/Staged).
$leftover = Get-AppxPackage -Name 'DietTracker*' -ErrorAction SilentlyContinue
if ($leftover) {
    Write-Warning "Package still present: $($leftover.PackageFullName) [$($leftover.Status)]"
    exit 1
}
Write-Host 'DietTracker is no longer installed.'