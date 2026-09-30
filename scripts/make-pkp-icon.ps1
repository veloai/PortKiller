# BUILD TOOL - not shipped. ASCII only (Windows PowerShell reads .ps1 as ANSI).
#
# Draws the one and only app icon: a rounded square with the letters PKP (PortKillerPet).
# Tray icon and exe icon are both made from this single picture (see make-ico.ps1).
#
# Usage: powershell -File make-pkp-icon.ps1 -Out assets/icons/pkp.png

param([Parameter(Mandatory=$true)][string]$Out)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$size = 1024
$bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g.Clear([System.Drawing.Color]::Transparent)

# rounded square, filled edge to edge so it stays readable at 16px
$r = 220
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$path.AddArc(0, 0, $r, $r, 180, 90)
$path.AddArc($size - $r, 0, $r, $r, 270, 90)
$path.AddArc($size - $r, $size - $r, $r, $r, 0, 90)
$path.AddArc(0, $size - $r, $r, $r, 90, 90)
$path.CloseFigure()

$rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
$c1 = [System.Drawing.Color]::FromArgb(255, 37, 99, 235)    # blue
$c2 = [System.Drawing.Color]::FromArgb(255, 124, 58, 237)   # violet
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 45.0)
$g.FillPath($brush, $path)

# PKP, white, as large as the square allows
$fmt = New-Object System.Drawing.StringFormat
$fmt.Alignment = [System.Drawing.StringAlignment]::Center
$fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
$font = New-Object System.Drawing.Font("Arial Black", 330, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$textRect = New-Object System.Drawing.RectangleF(0, 30, $size, $size)
$g.DrawString("PKP", $font, [System.Drawing.Brushes]::White, $textRect, $fmt)

$dir = Split-Path -Parent $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output "wrote: $Out"
