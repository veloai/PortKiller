using System.Runtime.InteropServices;

namespace PortKiller.App;

/// <summary>
/// 창 속성을 바꾸는 데 필요한 최소한의 윈도우 함수만 모았다.
///
/// 여기에 입력을 읽는 함수는 없다. 커서 좌표조차 읽지 않는다.
/// 클릭 통과는 투명 픽셀에서 윈도우가 알아서 처리하므로(실측 확인됨)
/// 마우스를 감시할 이유가 없다.
/// </summary>
internal static class NativeMethods
{
    private const int GWL_EXSTYLE = -20;

    /// <summary>Alt+Tab 목록과 작업 전환에서 빠진다. 펫은 "창"이 아니라 장식이므로.</summary>
    internal const int WS_EX_TOOLWINDOW = 0x0000_0080;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    /// <summary>창을 도구 창으로 표시한다. 창 핸들이 생긴 뒤에 부른다.</summary>
    internal static void MarkAsToolWindow(IntPtr hWnd)
    {
        var style = (int)GetWindowLongPtr(hWnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW;
        SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(style));
    }
}
