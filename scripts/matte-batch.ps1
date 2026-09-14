# TEST/BUILD TOOL - not shipped. ASCII only (Windows PowerShell reads .ps1 as ANSI).
#
# Strips the painted background off every raw image the generator produced and writes the
# real transparent PNG next to it, keeping the same folder shape:
#
#   assets/sprites/_raw/day/baby/idle.jpg  ->  assets/sprites/chars/day/baby/idle.png
#   assets/sprites/_raw/egg/idle.jpg       ->  assets/sprites/chars/egg/idle.png
#   assets/sprites/_raw/poop.jpg           ->  assets/sprites/poop.png
#
# Already-matted files are left alone unless -Force is given, so this is safe to run
# repeatedly while the generator is still filling in poses.

param([switch]$Force)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$raw = Join-Path $root "assets\sprites\_raw"
$matte = Join-Path $PSScriptRoot "matte-sprite.ps1"

if (-not (Test-Path $raw)) { Write-Output "no raw folder yet: $raw"; exit 0 }

$done = 0; $skipped = 0; $failed = 0

Get-ChildItem -Path $raw -Recurse -Include *.jpg, *.png | ForEach-Object {
    $rel = $_.FullName.Substring($raw.Length).TrimStart('\')
    $noExt = [IO.Path]::ChangeExtension($rel, $null).TrimEnd('.')

    # poop lives at the sprite root; everything else is a character under chars/
    if ($noExt -eq "poop") {
        $out = Join-Path $root "assets\sprites\poop.png"
    } else {
        $out = Join-Path $root ("assets\sprites\chars\" + $noExt + ".png")
    }

    if ((Test-Path $out) -and -not $Force) {
        $script:skipped++
        return
    }

    $dir = Split-Path -Parent $out
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

    try {
        & powershell -NoProfile -File $matte -In $_.FullName -Out $out | Out-Null
        Write-Output ("matted  " + $noExt)
        $script:done++
    } catch {
        Write-Output ("FAILED  " + $noExt + " : " + $_.Exception.Message)
        $script:failed++
    }
}

Write-Output ""
Write-Output "matted $done, already done $skipped, failed $failed"
if ($failed -gt 0) { exit 1 }
