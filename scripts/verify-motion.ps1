# TEST HARNESS ONLY - not shipped.
# Verifies the pet actually walks and that poops render.
#
# Colour matching alone is noisy on a busy desktop (IDE syntax highlighting is green too).
# The pet is a solid 96px circle, so we look for the LONGEST CONTIGUOUS HORIZONTAL RUN
# of its colour. Stray UI text produces short runs; the pet produces a ~90px one.

Add-Type -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class Shot
{
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

    public class Blob { public int X = -1; public int Y = -1; public int Run = 0; }

    // Longest contiguous horizontal run of the target colour, searched bottom-up.
    public static Blob LongestRun(Bitmap b, int tr, int tg, int tb, int tol)
    {
        Blob best = new Blob();
        for (int y = 0; y < b.Height; y++)
        {
            int run = 0, start = 0;
            for (int x = 0; x < b.Width; x++)
            {
                Color c = b.GetPixel(x, y);
                bool hit = Math.Abs(c.R - tr) <= tol && Math.Abs(c.G - tg) <= tol && Math.Abs(c.B - tb) <= tol;
                if (hit)
                {
                    if (run == 0) start = x;
                    run++;
                    if (run > best.Run) { best.Run = run; best.X = start; best.Y = y; }
                }
                else run = 0;
            }
        }
        return best;
    }

    public static Bitmap Grab(int w, int h)
    {
        Bitmap b = new Bitmap(w, h);
        using (Graphics g = Graphics.FromImage(b)) g.CopyFromScreen(0, 0, 0, 0, new Size(w, h));
        return b;
    }

    public static void Save(Bitmap b, string path) { b.Save(path, ImageFormat.Png); }
}
"@ -ReferencedAssemblies System.Drawing

[void][Shot]::SetProcessDPIAware()

$exe = "C:\Monster\Repo\PortKiller\src\PortKiller.App\bin\Debug\net9.0-windows\PortKillerPet.exe"
$W = 1920; $H = 1080
$out = @()

$proc = Start-Process -FilePath $exe -PassThru
try {
    Start-Sleep -Seconds 3
    $b1 = [Shot]::Grab($W, $H)
    $pet1  = [Shot]::LongestRun($b1, 0x7B, 0xC9, 0x6F, 10)
    $poop1 = [Shot]::LongestRun($b1, 0x8D, 0x6E, 0x4A, 10)
    [Shot]::Save($b1, "$env:TEMP\pk-motion-1.png")

    Start-Sleep -Seconds 8
    $b2 = [Shot]::Grab($W, $H)
    $pet2 = [Shot]::LongestRun($b2, 0x7B, 0xC9, 0x6F, 10)
    [Shot]::Save($b2, "$env:TEMP\pk-motion-2.png")

    $out += "pet blob t=3s  : x=$($pet1.X) y=$($pet1.Y) run=$($pet1.Run)px"
    $out += "pet blob t=11s : x=$($pet2.X) y=$($pet2.Y) run=$($pet2.Run)px"
    $out += "poop blob      : x=$($poop1.X) y=$($poop1.Y) run=$($poop1.Run)px"

    # A 96px circle yields a run of roughly 80-96px at its widest.
    $petFound = ($pet1.Run -ge 60) -and ($pet2.Run -ge 60)
    $shift = [Math]::Abs($pet2.X - $pet1.X)
    $moved = $shift -ge 5
    $poopFound = $poop1.Run -ge 25      # poop circles are 40px wide

    $out += ""
    $out += "pet rendered : $petFound"
    $out += "pet moved    : $moved  (shifted $shift px in 8s)"
    $out += "poop rendered: $poopFound"
    $out += ""
    if ($petFound -and $moved -and $poopFound) { $out += "RESULT: PASS" } else { $out += "RESULT: FAIL" }

    $b1.Dispose(); $b2.Dispose()
}
catch { $out += "ERROR: $_" }
finally {
    if ($proc -and -not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(3000) }
    $out += "app terminated"
}

$out | ForEach-Object { Write-Output $_ }
