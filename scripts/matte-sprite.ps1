# Sprite matting. ASCII only (Windows PowerShell reads .ps1 as ANSI).
#
# Why this exists: the image model cannot emit a real alpha channel. Asked for a
# transparent background it PAINTS the checkerboard pattern instead and returns JPEG.
# This script removes that painted background and produces a real transparent PNG.
#
# Method: flood fill from the image border over "bright and colourless" pixels
# (the checker is white / light grey). The character has a thick black outline,
# so the fill stops there. Cream body tones are saturated enough to survive.
# Alpha is binary (0 or 255) on purpose - PortKiller click-through reads alpha,
# and a soft edge would create invisible pixels that still block clicks.
#
# Usage: powershell -File matte-sprite.ps1 -In in.jpg -Out out.png [-Size 256]

param(
    [Parameter(Mandatory=$true)][string]$In,
    [Parameter(Mandatory=$true)][string]$Out,
    [int]$Size = 256
)

Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Bitmap]::FromFile((Resolve-Path $In))
$w = $src.Width; $h = $src.Height

# copy pixels into a flat array once - per-pixel GetPixel on a 1024px image is far too slow
$rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
$data = $src.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                      [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$bytes = New-Object byte[] ($w * $h * 4)
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
$src.UnlockBits($data)
$src.Dispose()

function Test-Background([int]$i, [int]$maxSat, [int]$minBright) {
    $b = $bytes[$i]; $g = $bytes[$i+1]; $r = $bytes[$i+2]
    $max = [Math]::Max($r, [Math]::Max($g, $b))
    $min = [Math]::Min($r, [Math]::Min($g, $b))
    return (($max - $min) -le $maxSat) -and ($max -ge $minBright)
}

$clear = New-Object bool[] ($w * $h)
$stack = New-Object System.Collections.Generic.Stack[int]

function Invoke-Flood([int]$maxSat, [int]$minBright) {
    for ($x = 0; $x -lt $w; $x++) {
        $stack.Push($x); $stack.Push(($h - 1) * $w + $x)
    }
    for ($y = 0; $y -lt $h; $y++) {
        $stack.Push($y * $w); $stack.Push($y * $w + $w - 1)
    }
    while ($stack.Count -gt 0) {
        $p = $stack.Pop()
        if ($clear[$p]) { continue }
        if (-not (Test-Background ($p * 4) $maxSat $minBright)) { continue }
        $clear[$p] = $true
        $px = $p % $w; $py = [int][Math]::Floor($p / $w)
        if ($px -gt 0)      { $stack.Push($p - 1) }
        if ($px -lt $w - 1) { $stack.Push($p + 1) }
        if ($py -gt 0)      { $stack.Push($p - $w) }
        if ($py -lt $h - 1) { $stack.Push($p + $w) }
    }
}

# pass 1: the checker itself (white 255 and grey ~204, both colourless)
Invoke-Flood 24 150
# pass 2: JPEG ringing left around the outline - looser, but still far from the
# black outline (bright) and from the cream body (colourless)
$seeded = $true
while ($seeded) {
    $seeded = $false
    for ($p = 0; $p -lt $w * $h; $p++) {
        if ($clear[$p]) { continue }
        if (-not (Test-Background ($p * 4) 48 130)) { continue }
        $px = $p % $w; $py = [int][Math]::Floor($p / $w)
        $touch = ($px -gt 0 -and $clear[$p-1]) -or ($px -lt $w-1 -and $clear[$p+1]) -or
                 ($py -gt 0 -and $clear[$p-$w]) -or ($py -lt $h-1 -and $clear[$p+$w])
        if ($touch) { $clear[$p] = $true; $seeded = $true }
    }
}

# bounding box of what survived
$minX = $w; $maxX = -1; $minY = $h; $maxY = -1
for ($p = 0; $p -lt $w * $h; $p++) {
    if ($clear[$p]) { continue }
    $px = $p % $w; $py = [int][Math]::Floor($p / $w)
    if ($px -lt $minX) { $minX = $px }
    if ($px -gt $maxX) { $maxX = $px }
    if ($py -lt $minY) { $minY = $py }
    if ($py -gt $maxY) { $maxY = $py }
}
if ($maxX -lt 0) { throw "nothing survived the matting - thresholds are wrong for this image" }

# write the cleared alpha back, then crop to the character
for ($p = 0; $p -lt $w * $h; $p++) {
    $bytes[$p * 4 + 3] = if ($clear[$p]) { 0 } else { 255 }
}
$full = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$fd = $full.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly,
                     [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
[System.Runtime.InteropServices.Marshal]::Copy($bytes, 0, $fd.Scan0, $bytes.Length)
$full.UnlockBits($fd)

$cw = $maxX - $minX + 1; $ch = $maxY - $minY + 1
$crop = $full.Clone((New-Object System.Drawing.Rectangle($minX, $minY, $cw, $ch)),
                    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$full.Dispose()

# square canvas: character fills ~92%, horizontally centred, FEET ON THE BOTTOM LINE.
# Every pose must share this baseline or the pet shakes vertically while walking.
$pad = [int]($Size * 0.04)
$inner = $Size - $pad * 2
$scale = [Math]::Min($inner / $cw, $inner / $ch)
$dw = [int]($cw * $scale); $dh = [int]($ch * $scale)
$dx = [int](($Size - $dw) / 2); $dy = $Size - $pad - $dh

$dst = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($dst)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.DrawImage($crop, $dx, $dy, $dw, $dh)
$g.Dispose(); $crop.Dispose()

# resampling reintroduces partial alpha at the edge - snap it back to binary so no
# invisible pixel can swallow a click
$dd = $dst.LockBits((New-Object System.Drawing.Rectangle(0, 0, $Size, $Size)),
                    [System.Drawing.Imaging.ImageLockMode]::ReadWrite,
                    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$ob = New-Object byte[] ($Size * $Size * 4)
[System.Runtime.InteropServices.Marshal]::Copy($dd.Scan0, $ob, 0, $ob.Length)
$opaque = 0
for ($i = 3; $i -lt $ob.Length; $i += 4) {
    if ($ob[$i] -ge 128) { $ob[$i] = 255; $opaque++ } else { $ob[$i] = 0 }
}
[System.Runtime.InteropServices.Marshal]::Copy($ob, 0, $dd.Scan0, $ob.Length)
$dst.UnlockBits($dd)

$dst.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$dst.Dispose()

$pct = [Math]::Round(100.0 * $opaque / ($Size * $Size), 1)
Write-Output "matted : $In -> $Out"
Write-Output "source : ${w}x${h}  character bbox ${cw}x${ch} at ${minX},${minY}"
Write-Output "output : ${Size}x${Size}  opaque ${opaque}px (${pct}%)  alpha is binary"
