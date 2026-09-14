# TEST HARNESS ONLY - not shipped. This script moves the cursor to probe the app;
# the app itself never reads cursor position. See docs/privacy-and-security.md.
# Click-through verification with NO cursor polling in the app (ASCII only).
# Proves per-pixel hit-testing alone gives correct behaviour, and measures CPU.

Add-Type -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class Probe
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    public static int ExStyle(IntPtr h) { return (int)GetWindowLongPtr(h, -20); }

    public static IntPtr FindByPid(uint target)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            if (!IsWindowVisible(h)) return true;
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid != target) return true;
            RECT r; GetWindowRect(h, out r);
            if (r.R - r.L < 200 || r.B - r.T < 200) return true;
            found = h; return false;
        }, IntPtr.Zero);
        return found;
    }

    public static IntPtr HitAt(int x, int y)
    {
        POINT p; p.X = x; p.Y = y;
        SetCursorPos(x, y);
        return WindowFromPoint(p);
    }

    public static void Shot(string path, int w, int h)
    {
        Bitmap b = new Bitmap(w, h);
        using (Graphics g = Graphics.FromImage(b))
            g.CopyFromScreen(0, 0, 0, 0, new Size(w, h));
        b.Save(path, ImageFormat.Png);
        b.Dispose();
    }
}
"@ -ReferencedAssemblies System.Drawing

$exe = "C:\Monster\Repo\PortKiller\src\PortKiller.App\bin\Debug\net9.0-windows\PortKillerPet.exe"
$saved = New-Object Probe+POINT
[void][Probe]::GetCursorPos([ref]$saved)

$proc = Start-Process -FilePath $exe -PassThru
$out = @()

try {
    Start-Sleep -Milliseconds 3000
    $h = [Probe]::FindByPid([uint32]$proc.Id)
    if ($h -eq [IntPtr]::Zero) { throw "pet window not found" }

    $r = New-Object Probe+RECT
    [void][Probe]::GetWindowRect($h, [ref]$r)
    $out += "pet window  : $h   rect $($r.L),$($r.T)-$($r.R),$($r.B)"
    $out += "ex-style    : 0x" + ("{0:X}" -f [Probe]::ExStyle($h)) + "   (0x20 = WS_EX_TRANSPARENT, expect absent)"
    $out += ""

    $cx = [int](($r.L + $r.R) / 2)

    # sample points: label, x, y, should the pet window receive the click?
    $cases = New-Object System.Collections.ArrayList
    [void]$cases.Add([pscustomobject]@{ N="empty center";      X=$cx;       Y=[int](($r.T+$r.B)/2); P=$false })
    [void]$cases.Add([pscustomobject]@{ N="empty left of pet"; X=$cx-300;   Y=[int]($r.B-56);       P=$false })
    [void]$cases.Add([pscustomobject]@{ N="empty between";     X=$cx+130;   Y=[int]($r.B-30);       P=$false })
    [void]$cases.Add([pscustomobject]@{ N="PET body";          X=$cx;       Y=[int]($r.B-56);       P=$true })
    [void]$cases.Add([pscustomobject]@{ N="POOP body";         X=$cx+224;   Y=[int]($r.B-32);       P=$true })
    [void]$cases.Add([pscustomobject]@{ N="debug panel";       X=[int]$r.L+60; Y=[int]$r.T+40;      P=$true })

    $allOk = $true
    foreach ($c in $cases) {
        $label = $c.N; $x = $c.X; $y = $c.Y; $expectPet = $c.P
        $hit = [Probe]::HitAt($x, $y)
        Start-Sleep -Milliseconds 150
        $isPet = ($hit -eq $h)
        $ok = ($isPet -eq $expectPet)
        if (-not $ok) { $allOk = $false }
        $verdict = $(if ($ok) { "OK  " } else { "BAD " })
        $target  = $(if ($expectPet) { "pet" } else { "pass-through" })
        $actual  = $(if ($isPet) { "pet" } else { "other($hit)" })
        $out += "$verdict $label at $x,$y -> expect $target, got $actual"
    }

    $shot = "$env:TEMP\portkiller-shot.png"
    [Probe]::Shot($shot, ($r.R - $r.L), ($r.B - $r.T))

    # CPU cost of a fullscreen transparent window over 5 seconds
    $proc.Refresh(); $t1 = $proc.TotalProcessorTime
    Start-Sleep -Seconds 5
    $proc.Refresh(); $t2 = $proc.TotalProcessorTime
    $cpuPct = [math]::Round((($t2 - $t1).TotalMilliseconds / 5000.0) * 100.0 / [Environment]::ProcessorCount, 2)
    $memMb = [math]::Round($proc.WorkingSet64 / 1MB, 1)

    $out += ""
    $out += "cpu over 5s : $cpuPct %  (all cores)"
    $out += "memory      : $memMb MB"
    $out += "screenshot  : $shot"
    $out += ""
    if ($allOk) { $out += "RESULT: PASS - per-pixel click-through works with no cursor polling" }
    else        { $out += "RESULT: FAIL" }
}
catch { $out += "ERROR: $_" }
finally {
    [void][Probe]::SetCursorPos($saved.X, $saved.Y)
    if ($proc -and -not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(3000) }
    $out += "app terminated"
}

$out | ForEach-Object { Write-Output $_ }

