# TEST HARNESS ONLY - not shipped. ASCII only (Windows PowerShell reads .ps1 as ANSI).
#
# Proves the sprites really render, not just that the files exist on disk.
#
# It asks the window to draw itself into a bitmap (PrintWindow with PW_RENDERFULLCONTENT)
# instead of screenshotting the desktop. The first version did compare desktop before/after
# and reported FAIL the day the Windows widget board happened to be open over the spot the
# pet stands on - the test was measuring the desktop, not the app.
#
# A pass means: the window drew a shape, that shape is a small patch rather than a filled
# rectangle (so the transparent area really is transparent), and it sits near the bottom.

Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win {
    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$exe = "C:\Monster\Repo\PortKiller\src\PortKiller.App\bin\Debug\net9.0-windows\PortKillerPet.exe"
$shot = "$env:TEMP\pk-sprites.png"
$out = @()
$proc = $null

try {
    $proc = Start-Process -FilePath $exe -PassThru
    Start-Sleep -Seconds 6

    $hwnd = [IntPtr]::Zero
    foreach ($h in (Get-Process -Id $proc.Id).Threads) { }
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $AE = [System.Windows.Automation.AutomationElement]
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)
    $wins = $AE::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
    foreach ($w in $wins) { $hwnd = [IntPtr]$w.Current.NativeWindowHandle; break }
    if ($hwnd -eq [IntPtr]::Zero) { throw "pet window not found" }

    $rect = New-Object Win+RECT
    [void][Win]::GetWindowRect($hwnd, [ref]$rect)
    $W = $rect.Right - $rect.Left
    $H = $rect.Bottom - $rect.Top

    $bmp = New-Object System.Drawing.Bitmap($W, $H, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    # 2 = PW_RENDERFULLCONTENT, needed for a composited (AllowsTransparency) window
    $ok = [Win]::PrintWindow($hwnd, $hdc, 2)
    $g.ReleaseHdc($hdc)
    $g.Dispose()
    if (-not $ok) { throw "PrintWindow refused to draw the window" }

    $data = $bmp.LockBits((New-Object System.Drawing.Rectangle(0, 0, $W, $H)),
                          [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                          [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $px = New-Object byte[] ($W * $H * 4)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $px, 0, $px.Length)
    $bmp.UnlockBits($data)

    # PrintWindow gives back an opaque surface, so "drawn" means "not the black it was
    # cleared to". Count that, and find where it sits.
    $drawn = 0; $lox = $W; $hix = -1; $loy = $H; $hiy = -1
    for ($y = 0; $y -lt $H; $y++) {
        for ($x = 0; $x -lt $W; $x++) {
            $i = ($y * $W + $x) * 4
            if (($px[$i] + $px[$i+1] + $px[$i+2]) -lt 24) { continue }
            $drawn++
            if ($x -lt $lox) { $lox = $x }
            if ($x -gt $hix) { $hix = $x }
            if ($y -lt $loy) { $loy = $y }
            if ($y -gt $hiy) { $hiy = $y }
        }
    }
    $bmp.Save($shot, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()

    $share = [Math]::Round(100.0 * $drawn / ($W * $H), 2)
    $out += "window     : ${W}x${H} at $($rect.Left),$($rect.Top)"
    $out += "drawn      : $drawn px (${share}% of the window)"
    if ($hix -ge 0) {
        $out += "drawn area : $lox,$loy -> $hix,$hiy  ($(($hix-$lox+1))x$(($hiy-$loy+1)))"
        $out += "bottom gap : $($H - 1 - $hiy) px above the window's bottom edge"
    }
    $out += "screenshot : $shot"
    $out += ""

    $something = $drawn -gt 500
    $notEverything = $share -lt 25
    $nearBottom = ($hiy -ge 0) -and (($H - 1 - $hiy) -lt 80)
    $out += "something drawn        : $something"
    $out += "most of it transparent : $notEverything"
    $out += "standing near the floor: $nearBottom"
    $out += ""
    if ($something -and $notEverything -and $nearBottom) { $out += "RESULT: PASS" }
    else { $out += "RESULT: FAIL" }
}
catch { $out += "ERROR: $_" }
finally {
    if ($proc -and -not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(3000) }
    $out += "app terminated"
}

$out | ForEach-Object { Write-Output $_ }
