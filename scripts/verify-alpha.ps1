# TEST HARNESS ONLY - not shipped. ASCII only (Windows PowerShell reads .ps1 as ANSI).
#
# Checks every shipped sprite for the one defect that breaks click-through silently:
# a pixel that is partly transparent. Those are invisible on screen but still swallow
# the click, so the user gets a dead zone around the pet with nothing to see there.
#
# Also reports the size and how much of the canvas the character fills, because a sprite
# that drifts off 256x256 or fills only half the frame will look wrong next to the others.

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$dirs = @((Join-Path $root "assets\sprites\chars"), (Join-Path $root "assets\sprites"))
$seen = @{}
$bad = 0
$count = 0
$out = @()

foreach ($d in $dirs) {
    if (-not (Test-Path $d)) { continue }
    Get-ChildItem -Path $d -Recurse -Filter *.png | ForEach-Object {
        if ($_.FullName -like "*_raw*" -or $_.FullName -like "*_candidates*") { return }
        if ($seen.ContainsKey($_.FullName)) { return }
        $seen[$_.FullName] = $true

        $b = [System.Drawing.Bitmap]::FromFile($_.FullName)
        $partial = 0; $opaque = 0
        $data = $b.LockBits((New-Object System.Drawing.Rectangle(0, 0, $b.Width, $b.Height)),
                            [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $bytes = New-Object byte[] ($b.Width * $b.Height * 4)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
        $b.UnlockBits($data)
        for ($i = 3; $i -lt $bytes.Length; $i += 4) {
            $a = $bytes[$i]
            if ($a -eq 255) { $opaque++ } elseif ($a -ne 0) { $partial++ }
        }
        $size = "$($b.Width)x$($b.Height)"
        $fill = [Math]::Round(100.0 * $opaque / ($b.Width * $b.Height), 1)
        $b.Dispose()

        $rel = $_.FullName.Substring($root.Length + 1)
        $count++
        if ($partial -gt 0 -or $size -ne "256x256") {
            $out += "BAD   $rel  $size  fill ${fill}%  partial-alpha ${partial}px"
            $script:bad++
        } else {
            $out += "ok    $rel  $size  fill ${fill}%"
        }
    }
}

$out += ""
$out += "checked $count sprite(s), $bad bad"
if ($bad -eq 0) { $out += "RESULT: PASS - every sprite is 256x256 with binary alpha" }
else { $out += "RESULT: FAIL" }
$out | ForEach-Object { Write-Output $_ }
if ($bad -gt 0) { exit 1 }
