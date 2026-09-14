# TEST HARNESS ONLY - not shipped. ASCII only (Windows PowerShell reads .ps1 as ANSI).
#
# Proves the sprites are actually on screen, not just on disk:
#   1. starts the app
#   2. grabs the strip of desktop the pet stands on
#   3. counts how many pixels of that strip are NOT the desktop behind it
#
# A blank strip means the art did not load and the emoji fallback (or nothing) is showing.

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$exe = "C:\Monster\Repo\PortKiller\src\PortKiller.App\bin\Debug\net9.0-windows\PortKillerPet.exe"
$shot = "$env:TEMP\pk-sprites.png"
$out = @()

# the whole bottom band of the work area - the pet can stand anywhere along it
$area = [System.Windows.Forms.SystemInformation]::WorkingArea
$W = $area.Width
$H = 260
$stripY = $area.Bottom - $H

$before = New-Object System.Drawing.Bitmap($W, $H)
$g = [System.Drawing.Graphics]::FromImage($before)
$proc = $null

try {
    $g.CopyFromScreen($area.Left, $stripY, 0, 0, (New-Object System.Drawing.Size($W, $H)))
    $g.Dispose()

    $proc = Start-Process -FilePath $exe -PassThru
    Start-Sleep -Seconds 6

    $after = New-Object System.Drawing.Bitmap($W, $H)
    $g2 = [System.Drawing.Graphics]::FromImage($after)
    $g2.CopyFromScreen($area.Left, $stripY, 0, 0, (New-Object System.Drawing.Size($W, $H)))
    $g2.Dispose()

    # count pixels that changed when the app appeared - that is the pet
    $changed = 0
    for ($y = 0; $y -lt $H; $y += 2) {
        for ($x = 0; $x -lt $W; $x += 2) {
            $a = $before.GetPixel($x, $y); $b = $after.GetPixel($x, $y)
            if ([Math]::Abs($a.R - $b.R) + [Math]::Abs($a.G - $b.G) + [Math]::Abs($a.B - $b.B) -gt 40) {
                $changed++
            }
        }
    }

    $after.Save($shot, [System.Drawing.Imaging.ImageFormat]::Png)
    $after.Dispose()

    $out += "strip          : ${W}x${H} across the bottom of the work area"
    $out += "changed pixels : $changed (sampled every 2nd pixel, so ~1/4 of the real count)"
    $out += "screenshot     : $shot"
    $out += ""
    if ($changed -gt 200) { $out += "RESULT: PASS - something was drawn where the pet stands" }
    else { $out += "RESULT: FAIL - the pet area is unchanged; the art did not render" }
}
catch { $out += "ERROR: $_" }
finally {
    $before.Dispose()
    if ($proc -and -not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(3000) }
    $out += "app terminated"
}

$out | ForEach-Object { Write-Output $_ }
