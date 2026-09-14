# TEST/BUILD TOOL - not shipped. ASCII only (Windows PowerShell reads .ps1 as ANSI).
#
# Builds a multi-size .ico from one square PNG.
#
# Why write the file by hand: System.Drawing can only save a SINGLE size as an icon, and
# Windows picks a different size for every place it draws one (16 in the tray and the title
# bar, 32 in Alt+Tab, 48 in the file list, 256 in large-icon view). Shipping one size means
# Windows scales it itself, and its scaler is worse than this one - a 256px drawing squeezed
# to 16px by the shell turns into orange mush.
#
# Frames 64px and under are stored as 32-bit DIBs, which every Windows version reads. The
# 256px frame is stored as PNG, which is how large icons have been stored since Vista and
# keeps the file from growing by 256KB for one frame.
#
# Alpha stays SOFT here on purpose - unlike the sprites, an icon is never click-through, and
# hard edges at 16px look ragged.
#
# Usage: powershell -File make-ico.ps1 -In head.png -Out app.ico [-Sizes 16,32,48,256]

param(
    [Parameter(Mandatory=$true)][string]$In,
    [Parameter(Mandatory=$true)][string]$Out,
    # A comma-separated string, not an int array: "powershell -File" hands every argument
    # over as one string, so an [int[]] parameter fails the moment this is run that way.
    [string]$Sizes = "16,20,24,32,40,48,64,128,256"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$sizeList = @($Sizes -split ',' | ForEach-Object { [int]$_.Trim() } | Sort-Object)

$src = [System.Drawing.Bitmap]::FromFile((Resolve-Path $In))

function Get-Frame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($src, 0, 0, $size, $size)
    $g.Dispose()
    return $bmp
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return , $bytes
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    $data = $bmp.LockBits((New-Object System.Drawing.Rectangle(0, 0, $size, $size)),
                          [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                          [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $px = New-Object byte[] ($size * $size * 4)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $px, 0, $px.Length)
    $bmp.UnlockBits($data)

    $maskRow = [int][Math]::Ceiling($size / 32.0) * 4
    $total = 40 + ($size * $size * 4) + ($maskRow * $size)
    $buf = New-Object byte[] $total
    $ms = New-Object System.IO.MemoryStream($buf, $true)
    $bw = New-Object System.IO.BinaryWriter($ms)

    # BITMAPINFOHEADER. Height is doubled because the structure covers the colour rows AND
    # the mask rows below them - that doubling is the classic trap in hand-written icons.
    $bw.Write([int]40)
    $bw.Write([int]$size)
    $bw.Write([int]($size * 2))
    $bw.Write([int16]1)
    $bw.Write([int16]32)
    $bw.Write([int]0)
    $bw.Write([int]($size * $size * 4))
    $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)

    # colour rows, bottom-up, BGRA - the same byte order LockBits handed us
    for ($y = $size - 1; $y -ge 0; $y--) {
        $row = $y * $size * 4
        $bw.Write($px, $row, $size * 4)
    }

    # 1bpp mask, bottom-up, 1 = transparent. 32-bit icons are drawn from the alpha channel,
    # but a missing or wrong mask still shows up as a black box in older shell surfaces.
    $maskLine = New-Object byte[] $maskRow
    for ($y = $size - 1; $y -ge 0; $y--) {
        [Array]::Clear($maskLine, 0, $maskRow)
        for ($x = 0; $x -lt $size; $x++) {
            if ($px[($y * $size + $x) * 4 + 3] -eq 0) {
                $byte = [int][Math]::Floor($x / 8)
                $maskLine[$byte] = $maskLine[$byte] -bor (0x80 -shr ($x % 8))
            }
        }
        $bw.Write($maskLine, 0, $maskRow)
    }

    $bw.Flush(); $bw.Dispose()
    return , $buf
}

$frames = @()
foreach ($size in $sizeList) {
    $bmp = Get-Frame $size
    # 256 as PNG, everything else as a DIB (see the header comment)
    $bytes = if ($size -ge 256) { Get-PngBytes $bmp } else { Get-DibBytes $bmp }
    $bmp.Dispose()
    $frames += , @{ size = $size; bytes = $bytes }
}
$src.Dispose()

# NOT $out - PowerShell variable names are case-insensitive, so that would be the same
# variable as the -Out parameter and would overwrite the destination path with a stream.
$icoStream = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($icoStream)
$bw.Write([int16]0)                 # reserved
$bw.Write([int16]1)                 # 1 = icon
$bw.Write([int16]$frames.Count)

$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    # 0 means 256 in this field - it is a single byte, so 256 does not fit
    $dim = if ($f.size -ge 256) { 0 } else { $f.size }
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)              # palette size (0 = no palette)
    $bw.Write([byte]0)              # reserved
    $bw.Write([int16]1)             # colour planes
    $bw.Write([int16]32)            # bits per pixel
    $bw.Write([int]$f.bytes.Length)
    $bw.Write([int]$offset)
    $offset += $f.bytes.Length
}
foreach ($f in $frames) { $bw.Write($f.bytes, 0, $f.bytes.Length) }
$bw.Flush()

$dir = Split-Path -Parent $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
[System.IO.File]::WriteAllBytes($Out, $icoStream.ToArray())
$bw.Dispose(); $icoStream.Dispose()

# read it back the way Windows will, so a malformed file fails here and not in the tray
$check = New-Object System.Drawing.Icon($Out)
$roundTrip = "$($check.Width)x$($check.Height)"
$check.Dispose()

Write-Output "wrote  : $Out"
Write-Output "frames : $($sizeList -join ', ')"
Write-Output "bytes  : $((Get-Item $Out).Length)"
Write-Output "reload : System.Drawing.Icon opened it, default frame ${roundTrip}"
