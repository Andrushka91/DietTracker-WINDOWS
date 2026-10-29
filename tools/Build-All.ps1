<#
.SYNOPSIS
    Builds both flavours of Diet Tracker with one command: the runnable application and the MSIX
    installer.

.DESCRIPTION
    Everything a test machine needs in one pass:

    1. builds and signs the MSIX bundle through tools\Update-Installer.ps1 -NoInstall, so the
       packaging, signing and artifact-cleanup rules live in exactly one place,
    2. rebuilds the unpackaged (WindowsPackageType=None) flavour afterwards and removes the packaging
       leftovers from the output folder, because the packaged build leaves an identity-bound
       DietTracker.exe there that cannot be started from disk (0x80040154 "Class not registered"),
    3. reports both outputs: dist\<exec|debug>\DietTracker.exe and the .msixbundle under artifacts\msix.

    The installer is built but NOT installed - installing is what `npm run installer` does (it signs
    the same 1.0.0.0 package and installs it over the previous version). Both commands stop
    a running instance first, because the build has to replace DietTracker.exe.

.PARAMETER Configuration
    Release (default) builds the optimized application into dist\exec; Debug builds the debuggable
    application into dist\debug.

.PARAMETER SkipInstaller
    Builds the runnable application only - the fast loop while working on the interface. This is what
    `npm run build:app` uses.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Build-All.ps1

.EXAMPLE
    npm run build

.EXAMPLE
    npm run build:app
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',

    [switch] $SkipInstaller
)

$ErrorActionPreference = 'Stop'

# Add-AppxPackage / packaging progress records are noise here.
$ProgressPreference = 'SilentlyContinue'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repositoryRoot

$projectFile = Join-Path $repositoryRoot 'src\DietTracker.App\DietTracker.App.csproj'
$distFolder = if ($Configuration -eq 'Debug') { 'debug' } else { 'exec' }
$outputDirectory = Join-Path $repositoryRoot "dist\$distFolder"
$executable = Join-Path $outputDirectory 'DietTracker.exe'

# A running instance locks DietTracker.exe; both the packaging step and the final build replace it.
$running = Get-Process -Name 'DietTracker' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Stopping $($running.Count) running Diet Tracker instance(s)..."
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 700
}

$totalSteps = if ($SkipInstaller) { 1 } else { 2 }
$step = 0

if (-not $SkipInstaller) {
    $step++
    Write-Host ''
    Write-Host ("{0}/{1}  Building the signed MSIX installer (artifacts\msix)..." -f $step, $totalSteps)

    # -KeepRunningApp: the instances were already stopped above. The script throws when the package
    # build or the signature check fails, which stops this script as well.
    & (Join-Path $PSScriptRoot 'Update-Installer.ps1') -Configuration $Configuration -NoInstall -KeepRunningApp
}

$step++
Write-Host ''
Write-Host ("{0}/{1}  Building the runnable (unpackaged) application..." -f $step, $totalSteps)

& dotnet build $projectFile -c $Configuration -p:Platform=x64 -m:1 -p:WindowsPackageType=None -p:EnableMsixTooling=false
if ($LASTEXITCODE -ne 0) {
    throw "The application build failed with exit code $LASTEXITCODE."
}

# The packaged build leaves an appx layout behind - a manifest next to an executable that only runs
# with package identity. Removing it keeps the output folder startable from disk and keeps
# tools\Run-App.ps1 from diagnosing a packaged layout that is not there any more.
foreach ($leftover in 'AppxManifest.xml', 'AppxBlockMap.xml', 'AppxSignature.pri') {
    $path = Join-Path $outputDirectory $leftover
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
        Write-Host ("Removed the packaging leftover $leftover from $outputDirectory")
    }
}

Write-Host ''
Write-Host 'Build finished.'
Write-Host ("  Application: " + $executable)

if (-not $SkipInstaller) {
    $bundle = Get-ChildItem (Join-Path $repositoryRoot 'artifacts\msix\DietTracker.App_*_Test\*.msixbundle') -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($bundle) {
        $hash = (Get-FileHash $bundle.FullName -Algorithm SHA256).Hash
        Write-Host ("  Installer:   " + $bundle.FullName)
        Write-Host ("  Size:        " + $bundle.Length + " bytes")
        Write-Host ("  SHA256:      " + $hash)
        Write-Host '  Install it:  npm run installer   (builds 1.0.0.0, signs and installs it)'
    } else {
        Write-Warning 'No .msixbundle was found under artifacts\msix after the build.'
    }
}

Write-Host ''
