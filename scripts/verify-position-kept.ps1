# TEST HARNESS ONLY - not shipped. ASCII only (Windows PowerShell reads .ps1 as ANSI).
#
# Checks that moving the pet actually sticks.
#
# Why: the pet used to be placed at the exact centre of the screen on every launch and its
# position was never written down. Moving it out of the way worked until you restarted, and
# then it was back in the middle of whatever you were reading - which reads to the user as
# "it cannot be moved".
#
# Steps: start, drag left, wait for a periodic save, kill WITHOUT a clean exit (that is the
# case that used to lose everything), start again, and see where it stands.

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Pos {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
}
"@

$exe = "C:\Monster\Repo\PortKiller\src\PortKiller.App\bin\Debug\net9.0-windows\PortKillerPet.exe"
$save = "$env:APPDATA\PortKillerPet\save.json"
$out = @()

function Start-Pet {
    $p = Start-Process -FilePath $exe -PassThru
    Start-Sleep -Seconds 9      # past the greeting bubble
    $AE = [System.Windows.Automation.AutomationElement]
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)
    $wins = $AE::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
    if ($wins.Count -eq 0) { throw "pet window not found" }
    return @{ proc = $p; hwnd = [IntPtr]$wins[0].Current.NativeWindowHandle
              rect = $wins[0].Current.BoundingRectangle }
}

function Get-PetCentreX($h, [int]$w, [int]$ht) {
    $bmp = New-Object System.Drawing.Bitmap($w, $ht, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc(); [void][Pos]::PrintWindow($h, $dc, 2); $g.ReleaseHdc($dc); $g.Dispose()
    $d = $bmp.LockBits((New-Object System.Drawing.Rectangle(0, 0, $w, $ht)),
                       [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                       [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $px = New-Object byte[] ($w * $ht * 4)
    [System.Runtime.InteropServices.Marshal]::Copy($d.Scan0, $px, 0, $px.Length)
    $bmp.UnlockBits($d); $bmp.Dispose()
    $lox = $w; $hix = -1
    for ($y = [int]($ht * 0.6); $y -lt $ht; $y++) {
        for ($x = 0; $x -lt $w; $x++) {
            $i = ($y * $w + $x) * 4
            if (($px[$i] + $px[$i+1] + $px[$i+2]) -lt 24) { continue }
            if ($x -lt $lox) { $lox = $x }
            if ($x -gt $hix) { $hix = $x }
        }
    }
    if ($hix -lt 0) { return -1 }
    return [int](($lox + $hix) / 2)
}

try {
    Get-Process PortKillerPet -ErrorAction SilentlyContinue | Stop-Process -Force
    if (Test-Path $save) { Rename-Item $save "$save.testbackup" -Force }
    Start-Sleep -Seconds 1

    # --- first run: where does a fresh pet stand? ---
    $one = Start-Pet
    $W = [int]$one.rect.Width; $H = [int]$one.rect.Height
    $fresh = Get-PetCentreX $one.hwnd $W $H
    $out += "fresh start   : pet centre x=$fresh  (screen centre is $([int]($W/2)))"

    # --- drag it left ---
    $cy = $H - 70
    $target = [Math]::Max(80, $fresh - 400)
    [void][Pos]::SetCursorPos($fresh, $cy)
    Start-Sleep -Milliseconds 300
    [Pos]::mouse_event([Pos]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 200
    for ($s = 1; $s -le 10; $s++) {
        [void][Pos]::SetCursorPos(($fresh + [int](($target - $fresh) * $s / 10.0)), $cy)
        Start-Sleep -Milliseconds 60
    }
    [Pos]::mouse_event([Pos]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 800
    $moved = Get-PetCentreX $one.hwnd $W $H
    $out += "after drag    : pet centre x=$moved"

    # --- wait for a periodic save, then KILL (no clean exit) ---
    $out += "waiting 65s for the periodic save, then killing the process outright..."
    Start-Sleep -Seconds 65
    $one.proc.Kill(); $one.proc.WaitForExit(3000)
    Start-Sleep -Seconds 2

    # --- second run ---
    $two = Start-Pet
    $again = Get-PetCentreX $two.hwnd $W $H
    $out += "after restart : pet centre x=$again"
    $two.proc.Kill(); $two.proc.WaitForExit(3000)

    $drift = [Math]::Abs($again - $moved)
    $notCentre = [Math]::Abs($fresh - [int]($W / 2)) -gt 200
    $out += ""
    $out += "drift over restart      : $drift px"
    $out += "fresh start off-centre  : $notCentre"
    $out += ""
    if ($drift -lt 40 -and $notCentre) { $out += "RESULT: PASS - the pet stays where it was put, and does not start in the middle" }
    else { $out += "RESULT: FAIL" }
}
catch { $out += "ERROR: $_" }
finally {
    Get-Process PortKillerPet -ErrorAction SilentlyContinue | Stop-Process -Force
    if (Test-Path "$save.testbackup") {
        if (Test-Path $save) { Remove-Item $save -Force }
        Rename-Item "$save.testbackup" $save -Force
        $out += "original save file restored"
    }
}

$out | ForEach-Object { Write-Output $_ }
