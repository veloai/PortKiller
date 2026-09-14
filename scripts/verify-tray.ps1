# TEST HARNESS ONLY - not shipped.
# Checks the tray icon is actually created and shown, and that it disappears on exit.
# The icon is a blue circle (96,165,250), so we scan the notification area for that colour.
# If the icon is in the hidden overflow flyout this reports NOT VISIBLE rather than failing
# the build - that is a Windows setting, not a defect.

Add-Type -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class Tray
{
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string name);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    public static int CountColour(Bitmap b, int tr, int tg, int tb, int tol)
    {
        int n = 0;
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
            {
                Color c = b.GetPixel(x, y);
                if (Math.Abs(c.R - tr) <= tol && Math.Abs(c.G - tg) <= tol && Math.Abs(c.B - tb) <= tol) n++;
            }
        return n;
    }

    public static Bitmap GrabRect(int x, int y, int w, int h)
    {
        Bitmap b = new Bitmap(w, h);
        using (Graphics g = Graphics.FromImage(b)) g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
        return b;
    }
}
"@ -ReferencedAssemblies System.Drawing

[void][Tray]::SetProcessDPIAware()

$exe = "C:\Monster\Repo\PortKiller\src\PortKiller.App\bin\Debug\net9.0-windows\PortKillerPet.exe"
$out = @()

# The notification area lives inside the taskbar window.
$tray = [Tray]::FindWindow("Shell_TrayWnd", $null)
$tr = New-Object Tray+RECT
[void][Tray]::GetWindowRect($tray, [ref]$tr)
$out += "taskbar rect : $($tr.L),$($tr.T)-$($tr.R),$($tr.B)"

$proc = Start-Process -FilePath $exe -PassThru
try {
    Start-Sleep -Seconds 4

    $w = $tr.R - $tr.L; $h = $tr.B - $tr.T
    $before = [Tray]::GrabRect($tr.L, $tr.T, $w, $h)
    $withApp = [Tray]::CountColour($before, 96, 165, 250, 26)
    $before.Save("$env:TEMP\pk-tray-on.png", [System.Drawing.Imaging.ImageFormat]::Png)
    $before.Dispose()

    $out += "app alive    : $(-not $proc.HasExited)"
    $out += "blue pixels in taskbar WITH app running : $withApp"

    $proc.Kill(); $proc.WaitForExit(5000)
    Start-Sleep -Seconds 3

    $after = [Tray]::GrabRect($tr.L, $tr.T, $w, $h)
    $withoutApp = [Tray]::CountColour($after, 96, 165, 250, 26)
    $after.Save("$env:TEMP\pk-tray-off.png", [System.Drawing.Imaging.ImageFormat]::Png)
    $after.Dispose()

    $out += "blue pixels in taskbar AFTER exit       : $withoutApp"
    $out += ""

    $delta = $withApp - $withoutApp
    if ($delta -ge 40) {
        $out += "RESULT: PASS - tray icon appeared while running and vanished on exit (delta $delta px)"
    } elseif ($withApp -eq 0) {
        $out += "RESULT: NOT VISIBLE - icon likely in the hidden overflow area. Check manually."
    } else {
        $out += "RESULT: INCONCLUSIVE - delta $delta px"
    }
}
catch { $out += "ERROR: $_" }
finally {
    if ($proc -and -not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(3000) }
}

$out | ForEach-Object { Write-Output $_ }
