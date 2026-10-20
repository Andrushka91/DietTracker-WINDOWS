<#
.SYNOPSIS
    Generates the DietTracker Store and tile assets (PNG) required by the MSIX package manifest.

.DESCRIPTION
    Draws the application mark at the sizes the Microsoft Store requires and writes them into
    src\DietTracker.App\Assets. The assets are deterministic: running the script again produces the
    same images, so the repository can be kept free of binary noise if preferred.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Generate-StoreAssets.ps1
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\src\DietTracker.App\Assets')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

# DietTracker brand colours (green = meals, blue = planning).
$backgroundColor = [System.Drawing.Color]::FromArgb(255, 15, 108, 189)
$accentColor = [System.Drawing.Color]::FromArgb(255, 16, 124, 16)

function New-DietTrackerImage {
    param(
        [int] $Width,
        [int] $Height,
        [string] $FileName,
        [bool] $WithMark = $true
    )

    $bitmap = New-Object System.Drawing.Bitmap($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias

    $graphics.Clear($backgroundColor)

    if ($WithMark) {
        $size = [Math]::Min($Width, $Height)
        $inset = [int]($size * 0.16)

        # Plate
        $plateBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
        $plateRect = New-Object System.Drawing.Rectangle($inset, $inset, ($size - 2 * $inset), ($size - 2 * $inset))
        $graphics.FillEllipse($plateBrush, $plateRect)

        # Inner accent circle
        $innerInset = [int]($size * 0.28)
        $innerBrush = New-Object System.Drawing.SolidBrush($accentColor)
        $innerRect = New-Object System.Drawing.Rectangle($innerInset, $innerInset, ($size - 2 * $innerInset), ($size - 2 * $innerInset))
        $graphics.FillEllipse($innerBrush, $innerRect)

        $plateBrush.Dispose()
        $innerBrush.Dispose()
    }

    $path = Join-Path $outputDirectory $FileName
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()

    Write-Host ("created {0} ({1}x{2})" -f $FileName, $Width, $Height)
}

New-DietTrackerImage -Width 50 -Height 50 -FileName 'StoreLogo.png'
New-DietTrackerImage -Width 44 -Height 44 -FileName 'Square44x44Logo.png'
New-DietTrackerImage -Width 150 -Height 150 -FileName 'Square150x150Logo.png'
New-DietTrackerImage -Width 310 -Height 150 -FileName 'Wide310x150Logo.png'
New-DietTrackerImage -Width 310 -Height 310 -FileName 'LargeTile.png'
New-DietTrackerImage -Width 620 -Height 300 -FileName 'SplashScreen.png'
New-DietTrackerImage -Width 1280 -Height 720 -FileName 'StoreScreenshot-1280x720.png' -WithMark $false

Write-Host "DietTracker assets written to $outputDirectory"
