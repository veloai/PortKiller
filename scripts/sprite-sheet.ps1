# TEST/BUILD TOOL - not shipped. ASCII only (Windows PowerShell reads .ps1 as ANSI).
#
# Builds one contact sheet of every matted sprite so a human can answer the three questions
# that only the eye can answer (docs/assets/image-brief.md section 7):
#
#   1. do all the poses look like the SAME character?
#   2. are the feet on the SAME baseline?  (a red line is drawn to check against)
#   3. is it still readable shrunk to 96px?  (each row is repeated small on a dark strip)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$chars = Join-Path $root "assets\sprites\chars"
$out = Join-Path $root "assets\sprites\_candidates\_sheet.png"

$rows = @(
    @{ label = "egg";   dir = Join-Path $chars "egg";           files = @("idle", "tilt1", "tilt2", "crack") }
    @{ label = "baby";  dir = Join-Path $chars "day\baby";      files = @("idle", "walk1", "walk2", "eat", "happy", "sick", "sleep", "sulk") }
    @{ label = "child"; dir = Join-Path $chars "day\child";     files = @("idle", "walk1", "walk2", "eat", "happy", "sick", "sleep", "sulk") }
    @{ label = "adult"; dir = Join-Path $chars "day\adult";     files = @("idle", "walk1", "walk2", "eat", "happy", "sick", "sleep", "sulk") }
)

$cell = 132      # 128px sprite + a little air
$small = 96      # the real on-screen size
$labelW = 64
$rowH = $cell + $small + 26
$W = $labelW + 8 * $cell + 12
$H = $rows.Count * $rowH + 12

$bmp = New-Object System.Drawing.Bitmap($W, $H, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.Clear([System.Drawing.Color]::White)

$font = New-Object System.Drawing.Font("Segoe UI", 9)
$bold = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$dark = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(28, 28, 30))
$line = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(220, 60, 60), 1)

$y = 6
foreach ($row in $rows) {
    # dark strip behind the small copies - the pet must read on a dark IDE too
    $g.FillRectangle($dark, $labelW, ($y + $cell), ($W - $labelW - 12), $small)
    $g.DrawString($row.label, $bold, [System.Drawing.Brushes]::Black, 6, ($y + $cell / 2 - 8))

    # the baseline every pose must share: bottom of the big cell
    $baseline = $y + $cell - 2
    $g.DrawLine($line, $labelW, $baseline, ($W - 12), $baseline)

    $x = $labelW
    foreach ($name in $row.files) {
        $file = Join-Path $row.dir ($name + ".png")
        if (Test-Path $file) {
            $img = [System.Drawing.Bitmap]::FromFile($file)
            $g.DrawImage($img, ($x + 2), ($y + 2), 128, 128)
            $g.DrawImage($img, ($x + 18), ($y + $cell), $small, $small)
            $img.Dispose()
            $g.DrawString($name, $font, [System.Drawing.Brushes]::Black, ($x + 4), ($y + $cell + $small + 4))
        } else {
            $g.DrawString($name + " (none)", $font, [System.Drawing.Brushes]::Red, ($x + 4), ($y + $cell / 2))
        }
        $x += $cell
    }
    $y += $rowH
}

$g.Dispose()
$dir = Split-Path -Parent $out
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "sheet: $out"
