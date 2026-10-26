<#
.SYNOPSIS
    Generates the DietTracker application icon, Store listing artwork (PNG) and the Windows .ico.

.DESCRIPTION
    Draws the DietTracker mark - the white meal plate with the green fork and knife on the
    brand-blue tile - at every size Windows needs and writes it into src\DietTracker.App\Assets:

    * the PNG logos referenced by Package.appxmanifest (Start menu entry, taskbar, tiles, splash)
    * DietTracker.ico (16-256 px), used as the executable icon (ApplicationIcon in the csproj)
      and as the notification-area icon

    The drawing is deterministic: running the script again produces the same bytes, so the
    repository stays free of binary noise. Every scene is rendered at up to 16x its final size
    and downsampled with high-quality interpolation (supersampling), which keeps the 44 px Start
    menu logo as crisp as the 620 px splash screen. The 16/24 px frames drop the cutlery - at that
    size it would blur into noise - and keep the plate with its solid meal marker instead.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Generate-StoreAssets.ps1
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# The default is resolved here instead of in the param block on purpose: Windows PowerShell 5.1
# does not populate $PSScriptRoot while it binds parameter defaults (Join-Path then fails on an
# empty path), and the body always has it.
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $PSScriptRoot '..\src\DietTracker.App\Assets'
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

# DietTracker brand colours (blue = planning, green = meals).
$script:tileTop = [System.Drawing.Color]::FromArgb(255, 27, 123, 199)    # brand blue, lit from above
$script:tileBottom = [System.Drawing.Color]::FromArgb(255, 11, 84, 150)  # brand blue, in shadow
$script:plateWhite = [System.Drawing.Color]::White
$script:accentGreen = [System.Drawing.Color]::FromArgb(255, 16, 124, 16)

# Supersample up to 16x, but never grow the working canvas past ~2048 px on the longest side:
# 44x44 renders at 704 px (16x) and the 1280x720 listing background stays at 1:1.
function Get-SupersampleFactor {
    param(
        [int] $Width,
        [int] $Height
    )

    $longest = [Math]::Max($Width, $Height)
    [Math]::Max(1, [Math]::Min(16, [int][Math]::Floor(2048 / $longest)))
}

function New-RoundedRectanglePath {
    param(
        [float] $X,
        [float] $Y,
        [float] $Width,
        [float] $Height,
        [float] $Radius
    )

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc(($X + $Width - $diameter), $Y, $diameter, $diameter, 270, 90)
    $path.AddArc(($X + $Width - $diameter), ($Y + $Height - $diameter), $diameter, $diameter, 0, 90)
    $path.AddArc($X, ($Y + $Height - $diameter), $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

# Fills the brand-blue tile; $Clip (the rounded rectangle of the .ico frames) cuts the corners
# away so the desktop icon gets the transparent silhouette Windows expects.
function Add-TileBackground {
    param(
        [System.Drawing.Graphics] $Graphics,
        [System.Drawing.RectangleF] $Bounds,
        [System.Drawing.Drawing2D.GraphicsPath] $Clip
    )

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($Bounds, $script:tileTop, $script:tileBottom, 90.0)
    try {
        if ($Clip) {
            $Graphics.FillPath($brush, $Clip)
        }
        else {
            $Graphics.FillRectangle($brush, $Bounds)
        }
    }
    finally {
        $brush.Dispose()
    }
}
# Draws the application mark inside $Box (a square region): the white plate with the green fork
# on the left and the tapered knife on the right - the plate reads as "meal", the cutlery as
# "tracker". $Simplified replaces the cutlery with the solid meal marker for the 16/24 px frames.
function Add-DietTrackerMark {
    param(
        [System.Drawing.Graphics] $Graphics,
        [System.Drawing.RectangleF] $Box,
        [bool] $Simplified
    )

    $side = [Math]::Min($Box.Width, $Box.Height)
    $cx = $Box.X + ($Box.Width / 2)
    $cy = $Box.Y + ($Box.Height / 2)
    $plateRadius = 0.31 * $side

    $plateBrush = New-Object System.Drawing.SolidBrush($script:plateWhite)
    $greenBrush = New-Object System.Drawing.SolidBrush($script:accentGreen)
    try {
        # Plate.
        $Graphics.FillEllipse($plateBrush, ($cx - $plateRadius), ($cy - $plateRadius), (2 * $plateRadius), (2 * $plateRadius))

        if ($Simplified) {
            $markerRadius = 0.15 * $side
            $Graphics.FillEllipse($greenBrush, ($cx - $markerRadius), ($cy - $markerRadius), (2 * $markerRadius), (2 * $markerRadius))
            return
        }

        $top = $cy - (0.215 * $side)
        $bottom = $cy + (0.215 * $side)

        # Fork: rounded head + capsule handle. The head is wider than the handle, which is what
        # separates the fork from the knife at a glance.
        $forkX = $cx - (0.085 * $side)
        $headWidth = 0.105 * $side
        $headPath = New-RoundedRectanglePath ($forkX - ($headWidth / 2)) $top $headWidth (0.17 * $side) (0.02 * $side)
        $Graphics.FillPath($greenBrush, $headPath)
        $headPath.Dispose()

        $handleWidth = 0.04 * $side
        $handleTop = $top + (0.13 * $side)
        $handlePath = New-RoundedRectanglePath ($forkX - ($handleWidth / 2)) $handleTop $handleWidth ($bottom - $handleTop) ($handleWidth / 2)
        $Graphics.FillPath($greenBrush, $handlePath)
        $handlePath.Dispose()

        # Two slots cut the head into three tines; they show the plate through, so painting them
        # plate-white is exact. Below ~0.6 px a slot drowns in the antialiasing, so the smallest
        # frames skip it and keep a solid (still fork-wide) head.
        $slotWidth = 0.017 * $side
        if ($slotWidth -ge 0.6) {
            $slotTop = $top + (0.008 * $side)
            $slotHeight = 0.107 * $side
            foreach ($offset in @(-0.0225, 0.0225)) {
                $slotX = $forkX + ($offset * $side)
                $Graphics.FillRectangle($plateBrush, ($slotX - ($slotWidth / 2)), $slotTop, $slotWidth, $slotHeight)
            }
        }

        # Knife: blade with a straight spine on the outside and a pointed tip, capsule handle.
        $knifeX = $cx + (0.085 * $side)
        $bladePoints = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(($knifeX - (0.035 * $side)), ($cy + (0.015 * $side))),
            [System.Drawing.PointF]::new(($knifeX - (0.035 * $side)), ($cy - (0.130 * $side))),
            [System.Drawing.PointF]::new(($knifeX + (0.002 * $side)), ($cy - (0.215 * $side))),
            [System.Drawing.PointF]::new(($knifeX + (0.035 * $side)), ($cy - (0.150 * $side))),
            [System.Drawing.PointF]::new(($knifeX + (0.035 * $side)), ($cy + (0.015 * $side)))
        )
        $Graphics.FillPolygon($greenBrush, $bladePoints)

        $knifeHandleWidth = 0.045 * $side
        $knifeHandlePath = New-RoundedRectanglePath ($knifeX - ($knifeHandleWidth / 2)) $cy $knifeHandleWidth ($bottom - $cy) ($knifeHandleWidth / 2)
        $Graphics.FillPath($greenBrush, $knifeHandlePath)
        $knifeHandlePath.Dispose()
    }
    finally {
        $plateBrush.Dispose()
        $greenBrush.Dispose()
    }
}
# Downsamples the supersampled working canvas to the final size and disposes the canvas.
function New-DownscaledBitmap {
    param(
        [System.Drawing.Bitmap] $Work,
        [int] $Width,
        [int] $Height
    )

    $final = New-Object System.Drawing.Bitmap($Width, $Height)
    $g = [System.Drawing.Graphics]::FromImage($final)
    try {
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.DrawImage($Work, 0, 0, $Width, $Height)
    }
    finally {
        $g.Dispose()
        $Work.Dispose()
    }
    return $final
}

function New-DietTrackerImage {
    param(
        [int] $Width,
        [int] $Height,
        [string] $FileName,
        [bool] $WithMark = $true
    )

    $factor = Get-SupersampleFactor $Width $Height
    $work = New-Object System.Drawing.Bitmap(($Width * $factor), ($Height * $factor))
    $g = [System.Drawing.Graphics]::FromImage($work)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        $bounds = New-Object System.Drawing.RectangleF(0, 0, $work.Width, $work.Height)
        Add-TileBackground $g $bounds $null

        if ($WithMark) {
            # Square logos fill the tile; wide/splash art centres the mark on the short side.
            $side = [Math]::Min($work.Width, $work.Height)
            $box = New-Object System.Drawing.RectangleF((($work.Width - $side) / 2), (($work.Height - $side) / 2), $side, $side)
            Add-DietTrackerMark $g $box ($side -le (24 * $factor))
        }
    }
    finally {
        $g.Dispose()
    }

    $final = New-DownscaledBitmap $work $Width $Height
    $final.Save((Join-Path $OutputDirectory $FileName), [System.Drawing.Imaging.ImageFormat]::Png)
    $final.Dispose()

    Write-Host ("created {0} ({1}x{2})" -f $FileName, $Width, $Height)
}

# One .ico frame: the same tile and mark, but with rounded, transparent corners - the desktop
# icon silhouette. The 16/24 px frames use the simplified mark (no cutlery).
function New-IconFrame {
    param([int] $Size)

    $factor = Get-SupersampleFactor $Size $Size
    $workSize = $Size * $factor
    $work = New-Object System.Drawing.Bitmap($workSize, $workSize)
    $g = [System.Drawing.Graphics]::FromImage($work)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)

        $bounds = New-Object System.Drawing.RectangleF(0, 0, $workSize, $workSize)
        $cornerPath = New-RoundedRectanglePath 0 0 $workSize $workSize (0.225 * $workSize)
        Add-TileBackground $g $bounds $cornerPath
        $cornerPath.Dispose()

        Add-DietTrackerMark $g $bounds ($Size -le 24)
    }
    finally {
        $g.Dispose()
    }

    return (New-DownscaledBitmap $work $Size $Size)
}
# Serialises one frame into the bytes of a .ico directory entry: BITMAPINFOHEADER, BGRA pixels
# bottom-up (the order the format expects) and the all-opaque AND mask - the 32-bit alpha channel
# is what actually cuts the transparent corners on Windows.
function ConvertTo-IconFrameBytes {
    param([System.Drawing.Bitmap] $Bitmap)

    $width = $Bitmap.Width
    $height = $Bitmap.Height
    $maskStride = ([int][Math]::Ceiling($width / 32)) * 4
    $xorBytes = $width * $height * 4
    $andBytes = $maskStride * $height

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    try {
        # BITMAPINFOHEADER (40 bytes).
        $writer.Write([uint32]40)
        $writer.Write([int32]$width)
        $writer.Write([int32]($height * 2))  # XOR bitmap + AND mask stacked
        $writer.Write([uint16]1)             # colour planes
        $writer.Write([uint16]32)            # bits per pixel
        $writer.Write([uint32]0)             # BI_RGB, no compression
        $writer.Write([uint32]($xorBytes + $andBytes))
        $writer.Write([int32]0)              # colours used
        $writer.Write([int32]0)              # colours important
        $writer.Write([uint32]0)             # palette
        $writer.Write([uint32]0)

        $rectangle = New-Object System.Drawing.Rectangle(0, 0, $width, $height)
        $data = $Bitmap.LockBits($rectangle, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $row = [byte[]]::new($width * 4)
            for ($y = $height - 1; $y -ge 0; $y--) {   # bottom-up
                [System.Runtime.InteropServices.Marshal]::Copy([IntPtr]::Add($data.Scan0, ($y * $data.Stride)), $row, 0, $row.Length)
                $writer.Write($row)
            }
        }
        finally {
            $Bitmap.UnlockBits($data)
        }

        $writer.Write([byte[]]::new($andBytes))
        $writer.Flush()
        return $stream.ToArray()
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

# Writes DietTracker.ico: one 32-bit entry per size, ascending.
function New-DietTrackerIcon {
    param(
        [string] $FileName,
        [int[]] $Sizes = @(16, 24, 32, 48, 64, 256)
    )

    $frames = [System.Collections.Generic.List[System.Drawing.Bitmap]]::new()
    $blobs = [System.Collections.Generic.List[byte[]]]::new()
    try {
        foreach ($size in $Sizes) {
            $frame = New-IconFrame $size
            $frames.Add($frame)
            $blobs.Add((ConvertTo-IconFrameBytes $frame))
        }

        $stream = [System.IO.File]::Open((Join-Path $OutputDirectory $FileName), [System.IO.FileMode]::Create)
        $writer = New-Object System.IO.BinaryWriter($stream)
        try {
            # ICONDIR: reserved, type (1 = icon), image count.
            $writer.Write([uint16]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]$frames.Count)

            $offset = 6 + (16 * $frames.Count)
            for ($i = 0; $i -lt $frames.Count; $i++) {
                $dimension = $frames[$i].Width
                $entrySize = if ($dimension -ge 256) { [byte]0 } else { [byte]$dimension }
                $writer.Write($entrySize)             # width (0 = 256)
                $writer.Write($entrySize)             # height (0 = 256)
                $writer.Write([byte]0)                # palette colours
                $writer.Write([byte]0)                # reserved
                $writer.Write([uint16]1)              # colour planes
                $writer.Write([uint16]32)             # bits per pixel
                $writer.Write([uint32]($blobs[$i].Length))
                $writer.Write([uint32]$offset)
                $offset += $blobs[$i].Length
            }

            foreach ($blob in $blobs) {
                $writer.Write($blob)
            }
        }
        finally {
            $writer.Dispose()
            $stream.Dispose()
        }
    }
    finally {
        foreach ($frame in $frames) {
            $frame.Dispose()
        }
    }

    Write-Host ("created {0} ({1} px)" -f $FileName, ($Sizes -join '/'))
}

New-DietTrackerImage -Width 50 -Height 50 -FileName 'StoreLogo.png'
New-DietTrackerImage -Width 44 -Height 44 -FileName 'Square44x44Logo.png'
New-DietTrackerImage -Width 150 -Height 150 -FileName 'Square150x150Logo.png'
New-DietTrackerImage -Width 310 -Height 150 -FileName 'Wide310x150Logo.png'
New-DietTrackerImage -Width 310 -Height 310 -FileName 'LargeTile.png'
New-DietTrackerImage -Width 620 -Height 300 -FileName 'SplashScreen.png'
New-DietTrackerImage -Width 1280 -Height 720 -FileName 'StoreScreenshot-1280x720.png' -WithMark $false
New-DietTrackerIcon -FileName 'DietTracker.ico'

Write-Host "DietTracker assets written to $OutputDirectory"