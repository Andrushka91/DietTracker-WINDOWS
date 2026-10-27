<#
.SYNOPSIS
    Builds Diet Tracker as an unpackaged application and starts it.

.DESCRIPTION
    Convenience wrapper around the two steps of the README section "Run the app": it builds the WinUI
    application with WindowsPackageType=None (the flavour whose DietTracker.exe can be started from disk),
    starts it and - when it exits immediately - prints the diagnosis instead of failing silently. The most
    common cause is an output folder that still holds a packaged (MSIX) layout: that executable only runs
    once the package is installed and exits at once with 0x80040154 "Class not registered".

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Run-App.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Run-App.ps1 -Configuration Debug

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Run-App.ps1 -NoBuild
#>
[CmdletBinding()]
param(
    # Build configuration. Extra words typed after "npm start" (like "npm start builed") land here,
    # so an unknown value is rejected below with a usage message instead of a validation error.
    [string] $Configuration = 'Release',

    # Starts what is already in the output folder without building first.
    [switch] $NoBuild,

    # Seconds the process must survive to count as started.
    [int] $SurvivalSeconds = 6
)

$ErrorActionPreference = 'Stop'

if ($Configuration -notin 'Debug', 'Release') {
    throw ("Unknown configuration '$Configuration'. " +
        'Usage: npm start (builds and runs the Release app), ' +
        'npm start -- -Configuration Debug (debug build), ' +
        'npm run build (build only), npm run start:debug (debug build and run).')
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $repositoryRoot 'src\DietTracker.App\DietTracker.App.csproj'
# The optimized application lives in dist/exec ("exec" = executable); dist/debug is the debuggable build.
$distFolder = if ($Configuration -eq 'Debug') { 'debug' } else { 'exec' }
$outputDirectory = Join-Path $repositoryRoot "dist\$distFolder"
$executable = Join-Path $outputDirectory 'DietTracker.exe'
$logFile = Join-Path $env:LOCALAPPDATA 'DietTracker\logs\diettracker.log'

if (-not $NoBuild) {
    # A running instance locks DietTracker.exe, so the build would fail to copy it. Stop it first.
    $running = Get-Process -Name 'DietTracker' -ErrorAction SilentlyContinue
    if ($running) {
        Write-Host "Stopping the running Diet Tracker instance(s) so the build can replace the executable..."
        $running | Stop-Process -Force
        Start-Sleep -Seconds 1
    }

    Write-Host "Building the unpackaged $Configuration application..."

    & dotnet build $projectFile -c $Configuration -p:Platform=x64 -m:1 -p:WindowsPackageType=None -p:EnableMsixTooling=false
    if ($LASTEXITCODE -ne 0) {
        throw "The build failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path $executable)) {
    throw "The application is not built: $executable. Run the script without -NoBuild first."
}

Write-Host "Starting $executable"
$process = Start-Process -FilePath $executable -WorkingDirectory $outputDirectory -PassThru

# The failure this script guards against is an immediate crash, so watch the process for a few seconds.
$deadline = [DateTime]::UtcNow.AddSeconds($SurvivalSeconds)
while ([DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 250
    $process.Refresh()
    if ($process.HasExited) {
        break
    }
}

if (-not $process.HasExited) {
    Write-Host "Diet Tracker is running (process $($process.Id)) - the window should be visible."
    Write-Host "Log file: $logFile"
    return
}

Write-Warning "Diet Tracker exited during start-up with code $($process.ExitCode) (0x$('{0:X8}' -f $process.ExitCode))."

if (Test-Path (Join-Path $outputDirectory 'AppxManifest.xml')) {
    Write-Warning @"
This output folder holds a PACKAGED (MSIX) layout. Its DietTracker.exe cannot be started from disk: it
needs the package identity that only an installed MSIX provides, which is why it exits with
0x80040154 "Class not registered".
    * rebuild the runnable flavour: run this script again without -NoBuild, or
    * build it by hand: dotnet build src/DietTracker.App/DietTracker.App.csproj -c $Configuration -p:Platform=x64 -m:1 -p:WindowsPackageType=None -p:EnableMsixTooling=false, or
    * install the package instead (README, "Packaged build").
"@
}

if (Test-Path $logFile) {
    Write-Host "Last lines of $logFile :"
    Get-Content $logFile -Tail 10 | ForEach-Object { Write-Host "  $_" }
}

Write-Host "Windows records the full reason too: Event Viewer > Windows Logs > Application (source '.NET Runtime')."
exit 1
