<#
.SYNOPSIS
    Rebuilds, signs and installs the Diet Tracker MSIX package (the local installer).

.DESCRIPTION
    One command that refreshes the installer after a change to the source:

    1. stops a running Diet Tracker (the build must replace dist\exec\DietTracker.exe),
    2. finds or creates the self-signed code signing certificate whose subject matches
       Publisher="CN=DietTracker" in Package.appxmanifest,
    3. builds the Release package (MSIX + bundle) with signing enabled,
    4. verifies the Authenticode signature of the produced bundle,
    5. installs it over the previous version with -ForceUpdateFromAnyVersion, so the package
       keeps the manifest's 1.0.0.0 and its data (LocalState) survives the refresh,
    6. prints the artifact path, size, build time and SHA-256.

    Run it after every change that has to be tested as an installed application; "npm run installer"
    is the shorthand.

.PARAMETER Configuration
    Build configuration of the package. Release is what the installer normally ships.

.PARAMETER NoInstall
    Builds and signs the package but does not install it.

.PARAMETER KeepRunningApp
    Leaves a running instance alone. The build fails if the executable is locked, so only use this
    when the process is known to be idle and closable by other means.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Update-Installer.ps1

.EXAMPLE
    npm run installer

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Update-Installer.ps1 -NoInstall
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',

    [switch] $NoInstall,

    [switch] $KeepRunningApp
)

$ErrorActionPreference = 'Stop'

# Add-AppxPackage writes one progress record per payload file; PowerShell renders every one of them
# into the console, which hides the output of this script.
$ProgressPreference = 'SilentlyContinue'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repositoryRoot

Write-Host "Refreshing the Diet Tracker installer ($Configuration, x64)..."

# 1. The deployment step would wait for a running application's graceful shutdown, and the build
#    cannot overwrite a locked executable anyway.
if (-not $KeepRunningApp) {
    $running = Get-Process -Name 'DietTracker' -ErrorAction SilentlyContinue
    if ($running) {
        Write-Host "Stopping $($running.Count) running Diet Tracker instance(s)..."
        $running | Stop-Process -Force
        Start-Sleep -Milliseconds 700
    }
}

# 2. Windows rejects unsigned packages and packages whose publisher chain cannot be verified, so the
#    certificate has to be the one Package.appxmanifest names.
$certificate = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq 'CN=DietTracker' -and $_.NotAfter -gt (Get-Date) } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if (-not $certificate) {
    Write-Host 'Creating the self-signed code signing certificate (CN=DietTracker)...'
    $certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=DietTracker' `
        -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(3)

    $artifactDirectory = Join-Path $repositoryRoot 'artifacts\msix'
    New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
    Export-Certificate -Cert $certificate -FilePath (Join-Path $artifactDirectory 'DietTracker.cer') | Out-Null

    Write-Warning 'A new certificate was created. Trust it once, in an elevated shell, before installing:'
    Write-Warning '  Import-Certificate -FilePath artifacts\msix\DietTracker.cer -CertStoreLocation Cert:\LocalMachine\Root'
    Write-Warning '  Import-Certificate -FilePath artifacts\msix\DietTracker.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople'
}

Write-Host ("Signing certificate: " + $certificate.Thumbprint)

# 3. The package version is the manifest version - 1.0.0.0 for every refresh. Windows normally
#    refuses to install a package whose version equals the installed one when the content differs
#    (0x80073CFB); that refusal used to force a new revision per refresh (1.0.0.1, 1.0.0.2, ...),
#    so the repository's single version drifted away from what is installed. The install below now
#    passes -ForceUpdateFromAnyVersion, which bypasses that version check: the package stays on the
#    tracked version, its data (LocalState) survives the refresh and the manifest never needs a
#    build-specific patch.
$manifestPath = Join-Path $repositoryRoot 'src\DietTracker.App\Package.appxmanifest'
$manifestText = [IO.File]::ReadAllText($manifestPath)
$versionMatch = [regex]::Match($manifestText, 'Version="(\d+\.\d+\.\d+\.\d+)"')
if (-not $versionMatch.Success) {
    throw 'Package.appxmanifest does not declare an x.y.z.w version.'
}

$buildVersion = [version]$versionMatch.Groups[1].Value
Write-Host ("Package version: " + $buildVersion)

# This compiles, packages and signs in one pass.
$project = Join-Path $repositoryRoot 'src\DietTracker.App\DietTracker.App.csproj'
dotnet build $project -c $Configuration -p:Platform=x64 -m:1 `
    -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=true `
    "-p:PackageCertificateThumbprint=$($certificate.Thumbprint)"

if ($LASTEXITCODE -ne 0) {
    throw "The package build failed with exit code $LASTEXITCODE."
}

# 4. The freshest bundle is the installer; a stale one from an earlier run must never be reported.
$bundle = Get-ChildItem (Join-Path $repositoryRoot 'artifacts\msix\DietTracker.App_*_Test\*.msixbundle') -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $bundle) {
    throw 'The build finished but no .msixbundle was produced.'
}

$signature = Get-AuthenticodeSignature $bundle.FullName
if ($signature.Status -ne 'Valid') {
    throw ("The generated package is not signed correctly: " + $signature.Status)
}

# 5. Install over what is installed. -ForceUpdateFromAnyVersion accepts the same (or an older)
#    version, which is what keeps the package at 1.0.0.0 instead of growing a new revision, and it
#    is an update, so the package data (LocalState) survives.
$package = $null
if (-not $NoInstall) {
    Write-Host 'Installing the package (the installed version is replaced in place, LocalState kept)...'
    try {
        Add-AppxPackage -Path $bundle.FullName -ForceApplicationShutdown -ForceUpdateFromAnyVersion
    } catch {
        throw ("The package could not be installed: " + $_.Exception.Message + "`r`n" +
            "If Windows still refuses the version, run 'npm run remove -- -KeepData' first " +
            "(it saves the package data) and rerun this script.")
    }
    $package = Get-AppxPackage -Name 'DietTracker.Desktop' -ErrorAction SilentlyContinue | Select-Object -First 1

    if (-not $package) {
        throw 'The package was built but Windows does not report it as installed.'
    }
}

# 6. Keep only the folder of the installer that was just produced (each refresh replaces the old one).
Get-ChildItem (Join-Path $repositoryRoot 'artifacts\msix') -Directory -Filter 'DietTracker.App_*_Test' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -ne $bundle.Directory.FullName } |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

# 7. Report exactly what was produced.
$hash = (Get-FileHash $bundle.FullName -Algorithm SHA256).Hash

Write-Host ''
Write-Host 'Installer refreshed.'
Write-Host ("  File:    " + $bundle.FullName)
Write-Host ("  Size:    " + $bundle.Length + " bytes")
Write-Host ("  Built:   " + $bundle.LastWriteTime)
Write-Host ("  SHA256:  " + $hash)
if ($package) {
    Write-Host ("  Package: " + $package.PackageFullName + " [" + $package.Status + "]")
    Write-Host ("  Launch:  explorer.exe shell:AppsFolder\" + $package.PackageFamilyName + "!DietTracker")
} else {
    Write-Host '  Package: not installed (-NoInstall)'
}

Write-Host ''
