# Sprite matting. ASCII only (Windows PowerShell reads .ps1 as ANSI).
#
# Why this exists: the image model cannot emit a real alpha channel
# (docs/assets/image-brief.md section 8). Asked for a transparent background it either
# PAINTS a grey/white checkerboard or drops the character on a flat coloured plate, and
# returns JPEG. This script removes that background and produces a real transparent PNG.
#
# Method: sample the colours actually present along the image border, then flood fill
# inwards over every pixel close to one of them. The character has a thick dark outline,
# so the fill stops there.
#
# Why sample instead of hardcoding: the first version assumed the checkerboard and tested
# for "bright and colourless". That silently did nothing on the images that came back with
# a tan, teal or mauve plate - four sprites shipped with a coloured square around them.
# Reading the border makes the rule work for whatever the model decided to paint.
#
# Alpha is binary (0 or 255) on purpose - PortKiller click-through reads alpha, and a soft
# edge would create pixels that are invisible but still swallow clicks.
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

# ---- 1. what colours does the border actually use? ----
# A checkerboard contributes two entries; a flat plate contributes one. A new entry is only
# opened when a border pixel is far from everything seen so far, so JPEG noise does not
# inflate the palette.
$palR = New-Object System.Collections.Generic.List[int]
$palG = New-Object System.Collections.Generic.List[int]
$palB = New-Object System.Collections.Generic.List[int]

function Add-Sample([int]$i) {
    $b = $bytes[$i]; $g = $bytes[$i+1]; $r = $bytes[$i+2]
    for ($k = 0; $k -lt $palR.Count; $k++) {
        $dr = $r - $palR[$k]; $dg = $g - $palG[$k]; $db = $b - $palB[$k]
        if (($dr*$dr + $dg*$dg + $db*$db) -le 900) { return }   # within 30 of a known colour
    }
    if ($palR.Count -lt 6) { $palR.Add($r); $palG.Add($g); $palB.Add($b) }
}

for ($x = 0; $x -lt $w; $x += 2) {
    Add-Sample (($x) * 4)
    Add-Sample ((($h - 1) * $w + $x) * 4)
}
for ($y = 0; $y -lt $h; $y += 2) {
    Add-Sample (($y * $w) * 4)
    Add-Sample (($y * $w + $w - 1) * 4)
}

function Test-Background([int]$i, [int]$tol) {
    $b = $bytes[$i]; $g = $bytes[$i+1]; $r = $bytes[$i+2]
    $limit = $tol * $tol
    for ($k = 0; $k -lt $palR.Count; $k++) {
        $dr = $r - $palR[$k]; $dg = $g - $palG[$k]; $db = $b - $palB[$k]
        if (($dr*$dr + $dg*$dg + $db*$db) -le $limit) { return $true }
    }
    return $false
}

# ---- 2. flood in from the border ----
$clear = New-Object bool[] ($w * $h)
$stack = New-Object System.Collections.Generic.Stack[int]

# Runs the fill again from scratch each time the palette grows.
#
# A "seen" mark is kept separately from "clear" on purpose: a second run starts on pixels
# that are ALREADY clear, and if those were skipped the fill would dead-end on its own
# seeds and spread nowhere (this is exactly what happened to the three eggs).
function Invoke-Flood {
    $seen = New-Object bool[] ($w * $h)
    for ($x = 0; $x -lt $w; $x++) { $stack.Push($x); $stack.Push((($h - 1) * $w) + $x) }
    for ($y = 0; $y -lt $h; $y++) { $stack.Push($y * $w); $stack.Push($y * $w + $w - 1) }

    while ($stack.Count -gt 0) {
        $p = $stack.Pop()
        if ($seen[$p]) { continue }
        $seen[$p] = $true
        if (-not $clear[$p] -and -not (Test-Background ($p * 4) 32)) { continue }
        $clear[$p] = $true
        $px = $p % $w; $py = [int][Math]::Floor($p / $w)
        if ($px -gt 0)      { $stack.Push($p - 1) }
        if ($px -lt $w - 1) { $stack.Push($p + 1) }
        if ($py -gt 0)      { $stack.Push($p - $w) }
        if ($py -lt $h - 1) { $stack.Push($p + $w) }
    }
}

# NOTE: PowerShell variable names are case-INSENSITIVE. $bx and $bX are the same variable -
# naming a min/max pair that way silently collapses the box to a single pixel. Hence x0/x1.
function Get-SurvivorBox {
    $x0 = $w; $x1 = -1; $y0 = $h; $y1 = -1
    for ($p = 0; $p -lt $w * $h; $p++) {
        if ($clear[$p]) { continue }
        $px = $p % $w; $py = [int][Math]::Floor($p / $w)
        if ($px -lt $x0) { $x0 = $px }
        if ($px -gt $x1) { $x1 = $px }
        if ($py -lt $y0) { $y0 = $py }
        if ($py -gt $y1) { $y1 = $py }
    }
    return , @($x0, $y0, $x1, $y1)
}

Invoke-Flood

# The model sometimes draws the subject on an INSET panel: a white page with a coloured
# card in the middle. The first flood eats the white and stops at the card, so the card
# ships as part of the sprite (three eggs did exactly this).
#
# So: look at the edge of whatever survived. If that edge is almost all one flat colour,
# it is another plate, not the character - add it to the palette and flood again.
# Bounded to two extra peels; a character outline is never a uniform rectangle edge,
# so this stops on its own.
for ($peel = 0; $peel -lt 2; $peel++) {
    $box = Get-SurvivorBox
    $x0 = [int]$box[0]; $y0 = [int]$box[1]; $x1 = [int]$box[2]; $y1 = [int]$box[3]
    if ($x1 -lt 0) { break }

    $er = 0; $eg = 0; $eb = 0; $n = 0; $matchInner = 0
    $edge = New-Object System.Collections.Generic.List[int]
    for ($x = $x0; $x -le $x1; $x += 2) { $edge.Add($y0 * $w + $x); $edge.Add($y1 * $w + $x) }
    for ($y = $y0; $y -le $y1; $y += 2) { $edge.Add($y * $w + $x0); $edge.Add($y * $w + $x1) }

    foreach ($p in $edge) {
        if ($clear[$p]) { continue }
        $i = $p * 4
        $er += $bytes[$i+2]; $eg += $bytes[$i+1]; $eb += $bytes[$i]; $n++
    }
    if ($n -lt 40) { break }
    $ar = [int]($er / $n); $ag = [int]($eg / $n); $ab = [int]($eb / $n)

    foreach ($p in $edge) {
        if ($clear[$p]) { continue }
        $i = $p * 4
        $dr = $bytes[$i+2] - $ar; $dg = $bytes[$i+1] - $ag; $db = $bytes[$i] - $ab
        if (($dr*$dr + $dg*$dg + $db*$db) -le 1024) { $matchInner++ }
    }

    # under 80% agreement it is a drawing, not a plate - leave it alone
    $agree = [Math]::Round(100.0 * $matchInner / $n)
    Write-Output "peel?  : survivor edge ${agree}% one colour ($ar,$ag,$ab) over $n samples"
    if ($matchInner -lt $n * 0.8) { break }

    $palR.Add($ar); $palG.Add($ag); $palB.Add($ab)
    Write-Output "peel   : inset plate ($ar,$ag,$ab) removed"
    Invoke-Flood
}

# ---- 3. peel the one blurred ring JPEG leaves against the outline ----
# Looser tolerance, but it only ever starts from pixels that already touch cleared ground,
# so it cannot appear inside the character.
#
# This grows from a frontier queue rather than rescanning the whole image until nothing
# changes. The rescan version worked but took minutes per sprite on a 1024px image - each
# extra ring cost another full million-pixel sweep.
$front = New-Object System.Collections.Generic.Stack[int]
for ($p = 0; $p -lt $w * $h; $p++) {
    if (-not $clear[$p]) { continue }
    $px = $p % $w; $py = [int][Math]::Floor($p / $w)
    if ($px -gt 0      -and -not $clear[$p-1])  { $front.Push($p-1) }
    if ($px -lt $w - 1 -and -not $clear[$p+1])  { $front.Push($p+1) }
    if ($py -gt 0      -and -not $clear[$p-$w]) { $front.Push($p-$w) }
    if ($py -lt $h - 1 -and -not $clear[$p+$w]) { $front.Push($p+$w) }
}
while ($front.Count -gt 0) {
    $p = $front.Pop()
    if ($clear[$p]) { continue }
    if (-not (Test-Background ($p * 4) 52)) { continue }
    $clear[$p] = $true
    $px = $p % $w; $py = [int][Math]::Floor($p / $w)
    if ($px -gt 0)      { $front.Push($p - 1) }
    if ($px -lt $w - 1) { $front.Push($p + 1) }
    if ($py -gt 0)      { $front.Push($p - $w) }
    if ($py -lt $h - 1) { $front.Push($p + $w) }
}

# ---- 4. crop to what survived ----
$minX = $w; $maxX = -1; $minY = $h; $maxY = -1
for ($p = 0; $p -lt $w * $h; $p++) {
    if ($clear[$p]) { continue }
    $px = $p % $w; $py = [int][Math]::Floor($p / $w)
    if ($px -lt $minX) { $minX = $px }
    if ($px -gt $maxX) { $maxX = $px }
    if ($py -lt $minY) { $minY = $py }
    if ($py -gt $maxY) { $maxY = $py }
}
if ($maxX -lt 0) { throw "nothing survived the matting - the whole image matched the border colour" }

$kept = ($maxX - $minX + 1) * ($maxY - $minY + 1)
if ($kept -gt ($w * $h * 0.92)) {
    throw "background was not removed (kept $kept of $($w*$h) px) - the border colour did not separate"
}

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

# ---- 5. square canvas: ~92% fill, centred, FEET ON THE BOTTOM LINE ----
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
Write-Output "border : $($palR.Count) colour(s) sampled"
Write-Output "source : ${w}x${h}  character bbox ${cw}x${ch} at ${minX},${minY}"
Write-Output "output : ${Size}x${Size}  opaque ${opaque}px (${pct}%)  alpha is binary"
