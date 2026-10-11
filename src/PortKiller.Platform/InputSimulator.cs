using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PortKiller.Platform;

/// <summary>
/// 윈도우 저수준 가상 입력(SendInput) 전송기.
/// 
/// 게임 컨트롤러 및 음성 명령에서 발생한 이벤트를
/// 윈도우 포그라운드 게임 프로세스로 전달한다.
/// 
/// 보안 및 안티치트 준수 원칙:
///   - 1 입력 = 1 출력 원칙 (자동 연타, 루프, 메모리 변조 없음)
///   - 타겟 프로세스가 활성화(Foreground) 상태일 때만 동작하여 오작동 방지
/// </summary>
public static class InputSimulator
{
    private const int INPUT_MOUSE = 0;
    private const int INPUT_KEYBOARD = 1;

    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;

    /// <summary>
    /// 현재 활성화된 포그라운드 윈도우의 프로세스 이름을 반환한다. (예: "WowClassic", "Wow", "chrome")
    /// </summary>
    public static string? GetForegroundProcessName()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        _ = GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0) return null;

        try
        {
            using var proc = Process.GetProcessById((int)processId);
            return proc.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 마우스 커서를 상대 좌표(dx, dy)만큼 이동시킨다. (터치패드 / 아날로그 스틱 에임용)
    /// </summary>
    public static void MoveMouseRelative(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    dwFlags = MOUSEEVENTF_MOVE,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// 마우스 좌클릭 (누르기 또는 떼기)
    /// </summary>
    public static void MouseLeftClick(bool isDown)
    {
        SendMouseEvent(isDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP);
    }

    /// <summary>
    /// 마우스 우클릭 (누르기 또는 떼기)
    /// </summary>
    public static void MouseRightClick(bool isDown)
    {
        SendMouseEvent(isDown ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP);
    }

    private static void SendMouseEvent(uint flag)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = flag,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// 단일 가상 키(Virtual Key Code) 누름 또는 뗌.
    /// </summary>
    public static void SendKey(ushort vkCode, bool isDown)
    {
        var scanCode = (ushort)MapVirtualKey(vkCode, 0);
        var flags = KEYEVENTF_SCANCODE;
        if (!isDown) flags |= KEYEVENTF_KEYUP;

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = scanCode,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// 키보드 키를 1회 탭(Tap)한다. (보이스 명령 등의 단발성 스킬 발동)
    /// </summary>
    public static void TapKey(ushort vkCode, ushort modifierVk = 0)
    {
        if (modifierVk != 0)
        {
            SendKey(modifierVk, true);
        }

        SendKey(vkCode, true);
        Thread.Sleep(20);
        SendKey(vkCode, false);

        if (modifierVk != 0)
        {
            SendKey(modifierVk, false);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
