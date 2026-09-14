# TEST HARNESS ONLY - not shipped. ASCII only (Windows PowerShell reads .ps1 as ANSI).
#
# Drags the pet with the real mouse and checks it moved.
#
# Why this test exists: setting the pet's Background to null while wiring the artwork made
# WPF stop treating it as a mouse target - the pet could no longer be picked up or clicked,
# and nothing in the build or the unit tests noticed. Only an actual drag catches that.
#
# It finds the pet by scanning the window's own rendering (PrintWindow) for drawn pixels,
# so it does not care where the pet happens to be standing or what is on the desktop.

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Drag {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
}
"@

$exe = "C:\Monster\Repo\PortKiller\src\PortKiller.App\bin\Debug\net9.0-windows\PortKillerPet.exe"
$out = @()
$proc = $null

function Get-PetBox([IntPtr]$hwnd, [int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc()
    [void][Drag]::PrintWindow($hwnd, $dc, 2)
    $g.ReleaseHdc($dc); $g.Dispose()

    $d = $bmp.LockBits((New-Object System.Drawing.Rectangle(0, 0, $w, $h)),
                       [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                       [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $px = New-Object byte[] ($w * $h * 4)
    [System.Runtime.InteropServices.Marshal]::Copy($d.Scan0, $px, 0, $px.Length)
    $bmp.UnlockBits($d); $bmp.Dispose()

    # Only look at the bottom band: the debug panel sits in the top-left corner and would
    # otherwise be counted as "the pet" and never move.
    $lox = $w; $hix = -1; $loy = $h; $hiy = -1
    for ($y = [int]($h * 0.6); $y -lt $h; $y++) {
        for ($x = 0; $x -lt $w; $x++) {
            $i = ($y * $w + $x) * 4
            if (($px[$i] + $px[$i+1] + $px[$i+2]) -lt 24) { continue }
            if ($x -lt $lox) { $lox = $x }
            if ($x -gt $hix) { $hix = $x }
            if ($y -lt $loy) { $loy = $y }
            if ($y -gt $hiy) { $hiy = $y }
        }
    }
    return , @($lox, $loy, $hix, $hiy)
}

try {
    $proc = Start-Process -FilePath $exe -PassThru
    Start-Sleep -Seconds 9   # let the greeting speech bubble expire (up to 7s) - it would widen the pet box

    $AE = [System.Windows.Automation.AutomationElement]
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)
    $wins = $AE::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
    if ($wins.Count -eq 0) { throw "pet window not found" }
    $win = $wins[0]
    $hwnd = [IntPtr]$win.Current.NativeWindowHandle
    $r = $win.Current.BoundingRectangle
    $W = [int]$r.Width; $H = [int]$r.Height

    $before = Get-PetBox $hwnd $W $H
    if ($before[2] -lt 0) { throw "nothing drawn in the lower part of the window" }
    $cx = [int](($before[0] + $before[2]) / 2)
    $cy = [int](($before[1] + $before[3]) / 2)
    $out += "pet before : centre $cx,$cy  box $($before[0]),$($before[1]) -> $($before[2]),$($before[3])"

    # drag it 300px to the left, in steps - one jump can be read as a click
    $targetX = [Math]::Max(60, $cx - 300)
    [void][Drag]::SetCursorPos(($cx + [int]$r.X), ($cy + [int]$r.Y))
    Start-Sleep -Milliseconds 300
    [Drag]::mouse_event([Drag]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 200
    for ($s = 1; $s -le 10; $s++) {
        $x = $cx + [int](($targetX - $cx) * $s / 10.0)
        [void][Drag]::SetCursorPos(($x + [int]$r.X), ($cy + [int]$r.Y))
        Start-Sleep -Milliseconds 60
        if ($s -eq 5) {
            $mid = Get-PetBox $hwnd $W $H
            $out += "  mid-drag : cursor at $x, pet centre $([int](($mid[0]+$mid[2])/2))"
        }
    }
    [Drag]::mouse_event([Drag]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 800

    $after = Get-PetBox $hwnd $W $H
    $ax = [int](($after[0] + $after[2]) / 2)
    $ay = [int](($after[1] + $after[3]) / 2)
    $out += "pet after  : centre $ax,$ay  box $($after[0]),$($after[1]) -> $($after[2]),$($after[3])"

    $moved = [Math]::Abs($ax - $cx)
    $out += "moved      : $moved px horizontally (asked for $([Math]::Abs($targetX - $cx)))"
    $out += ""
    if ($moved -gt 100) { $out += "RESULT: PASS - the pet can be picked up and moved" }
    else { $out += "RESULT: FAIL - the pet did not move; it is not receiving the mouse" }
}
catch { $out += "ERROR: $_" }
finally {
    if ($proc -and -not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(3000) }
    $out += "app terminated"
}

$out | ForEach-Object { Write-Output $_ }
