# TEST HARNESS ONLY - not shipped.
# Drives the care menu end to end: greeting bubble on start, right-click opens the
# menu as its own popup window, choosing an item feeds the pet and shows a reply.

Add-Type -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

public static class Care
{
    public delegate bool EP(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool EnumWindows(EP cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);

    public const uint RIGHTDOWN = 0x0008, RIGHTUP = 0x0010, LEFTDOWN = 0x0002, LEFTUP = 0x0004;

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    public static void RightClick(int x, int y)
    {
        SetCursorPos(x, y);
        mouse_event(RIGHTDOWN, 0, 0, 0, IntPtr.Zero);
        mouse_event(RIGHTUP, 0, 0, 0, IntPtr.Zero);
    }

    public static void LeftClick(int x, int y)
    {
        SetCursorPos(x, y);
        mouse_event(LEFTDOWN, 0, 0, 0, IntPtr.Zero);
        mouse_event(LEFTUP, 0, 0, 0, IntPtr.Zero);
    }

    // The pet window covers the screen; the context menu is a separate small popup.
    public static string ListWindows(uint pid)
    {
        var sb = new StringBuilder();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            if (!IsWindowVisible(h)) return true;
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid) return true;
            RECT r; GetWindowRect(h, out r);
            sb.AppendLine((r.R - r.L) + "x" + (r.B - r.T) + " at " + r.L + "," + r.T);
            return true;
        }, IntPtr.Zero);
        return sb.ToString();
    }

    // The menu may open above or below the cursor depending on room; read its real rect.
    public static RECT FindPopup(uint pid, int maxW, int maxH)
    {
        RECT found = new RECT();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            if (!IsWindowVisible(h)) return true;
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid) return true;
            RECT r; GetWindowRect(h, out r);
            int w = r.R - r.L, ht = r.B - r.T;
            if (w > 20 && w <= maxW && ht > 20 && ht <= maxH) { found = r; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static int CountWindows(uint pid, int maxW, int maxH)
    {
        int n = 0;
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            if (!IsWindowVisible(h)) return true;
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid) return true;
            RECT r; GetWindowRect(h, out r);
            int w = r.R - r.L, ht = r.B - r.T;
            if (w > 20 && w <= maxW && ht > 20 && ht <= maxH) n++;
            return true;
        }, IntPtr.Zero);
        return n;
    }

    // Longest horizontal run of a colour, limited to a vertical band.
    public static int LongestRun(Bitmap b, int y0, int y1, int tr, int tg, int tb, int tol)
    {
        int best = 0;
        for (int y = Math.Max(0, y0); y < Math.Min(b.Height, y1); y++)
        {
            int run = 0;
            for (int x = 0; x < b.Width; x++)
            {
                Color c = b.GetPixel(x, y);
                if (Math.Abs(c.R - tr) <= tol && Math.Abs(c.G - tg) <= tol && Math.Abs(c.B - tb) <= tol)
                { run++; if (run > best) best = run; }
                else run = 0;
            }
        }
        return best;
    }

    public static Bitmap Grab(int x, int y, int w, int h)
    {
        Bitmap b = new Bitmap(w, h);
        using (Graphics g = Graphics.FromImage(b)) g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
        return b;
    }
    public static void Save(Bitmap b, string p) { b.Save(p, ImageFormat.Png); }
}
"@ -ReferencedAssemblies System.Drawing

[void][Care]::SetProcessDPIAware()

$exe = "C:\Monster\Repo\PortKiller\src\PortKiller.App\bin\Debug\net9.0-windows\PortKillerPet.exe"
$W = 1920; $H = 1080
$out = @()
$proc = Start-Process -FilePath $exe -PassThru

try {
    Start-Sleep -Seconds 3
    $pid32 = [uint32]$proc.Id

    # --- 1. greeting bubble on start (white rounded box above the pet) ---
    $b1 = [Care]::Grab(0, 0, $W, $H)
    # pet stands at the bottom; the bubble sits just above it
    $bubbleRun = [Care]::LongestRun($b1, 800, 930, 255, 255, 255, 6)
    [Care]::Save($b1, "$env:TEMP\pk-care-1.png")
    $b1.Dispose()
    $out += "greeting bubble white run : $bubbleRun px  (need >= 80)"

    # --- 2. right-click the pet -> context menu should open as its own window ---
    $before = [Care]::CountWindows($pid32, 700, 700)
    [Care]::RightClick(960, 976)
    Start-Sleep -Milliseconds 900
    $after = [Care]::CountWindows($pid32, 700, 700)
    $out += "popup windows before/after right-click : $before / $after"
    $out += "--- windows owned by app after right-click ---"
    $out += ([Care]::ListWindows($pid32)).TrimEnd()

    $b2 = [Care]::Grab(0, 0, $W, $H)
    [Care]::Save($b2, "$env:TEMP\pk-care-2.png")
    $b2.Dispose()

    # --- 3. choose the first item (feed) ---
    # The menu can open above the cursor when there is no room below, so read its rect.
    $menu = [Care]::FindPopup($pid32, 700, 700)
    $out += "menu rect : $($menu.L),$($menu.T)-$($menu.R),$($menu.B)"
    $itemX = $menu.L + 50
    $itemY = $menu.T + 14
    $out += "clicking first item at $itemX,$itemY"
    [Care]::LeftClick($itemX, $itemY)
    Start-Sleep -Milliseconds 1200

    $b3 = [Care]::Grab(0, 0, $W, $H)
    $replyRun = [Care]::LongestRun($b3, 800, 930, 255, 255, 255, 6)
    [Care]::Save($b3, "$env:TEMP\pk-care-3.png")
    $b3.Dispose()
    $out += "reply bubble white run    : $replyRun px  (need >= 80)"

    $menuOpened = $after -gt $before
    $greeted = $bubbleRun -ge 80
    $replied = $replyRun -ge 80

    $out += ""
    $out += "greeting shown : $greeted"
    $out += "menu opened    : $menuOpened"
    $out += "reply shown    : $replied"
    $out += ""
    if ($greeted -and $menuOpened -and $replied) { $out += "RESULT: PASS" } else { $out += "RESULT: FAIL" }
}
catch { $out += "ERROR: $_" }
finally {
    if ($proc -and -not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(3000) }
    $out += "app terminated"
}

$out | ForEach-Object { Write-Output $_ }
