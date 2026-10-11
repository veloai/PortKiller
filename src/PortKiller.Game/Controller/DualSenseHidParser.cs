using PortKiller.Game.Models;

namespace PortKiller.Game.Controller;

/// <summary>
/// PS5 듀얼센스의 표준 USB / Bluetooth HID 입력 리포트를 파싱하는 순수 파서.
/// 
/// 하드웨어 의존성이 없으므로 단위 테스트(Unit Test)로 100% 검증 가능합니다.
/// </summary>
public static class DualSenseHidParser
{
    public const int VendorId = 0x054C;       // Sony
    public const int ProductIdStandard = 0x0CE6; // DualSense Wireless Controller
    public const int ProductIdEdge = 0x0DF2;     // DualSense Edge

    /// <summary>
    /// 수신된 raw 바이트 배열로부터 DualSenseState 를 생성합니다.
    /// </summary>
    public static DualSenseState Parse(ReadOnlySpan<byte> report)
    {
        var state = new DualSenseState { IsConnected = true };
        if (report.Length < 11) return state;

        // Bluetooth 리포트(0x31)는 앞에 1~2바이트 오프셋이 붙음
        int offset = 0;
        if (report[0] == 0x31 && report.Length >= 78)
        {
            offset = 1;
        }

        // 1. 아날로그 스틱 (0~255, 중립 128) -> -1.0 ~ 1.0 정규화
        state.LeftStickX = NormalizeAxis(report[offset + 1]);
        state.LeftStickY = NormalizeAxis(report[offset + 2]);
        state.RightStickX = NormalizeAxis(report[offset + 3]);
        state.RightStickY = NormalizeAxis(report[offset + 4]);

        // 2. 아날로그 트리거 (0~255) -> 0.0 ~ 1.0
        state.L2Trigger = report[offset + 5] / 255.0;
        state.R2Trigger = report[offset + 6] / 255.0;

        // 3. D-Pad (방향키, 0~7) & 액션 버튼
        byte b8 = report[offset + 8];
        byte dpad = (byte)(b8 & 0x0F);
        ParseDpad(dpad, state.PressedButtons);

        if ((b8 & 0x10) != 0) state.PressedButtons.Add(DualSenseButton.Square);
        if ((b8 & 0x20) != 0) state.PressedButtons.Add(DualSenseButton.Cross);
        if ((b8 & 0x40) != 0) state.PressedButtons.Add(DualSenseButton.Circle);
        if ((b8 & 0x80) != 0) state.PressedButtons.Add(DualSenseButton.Triangle);

        // 4. 범퍼 및 스틱 클릭
        byte b9 = report[offset + 9];
        if ((b9 & 0x01) != 0) state.PressedButtons.Add(DualSenseButton.L1);
        if ((b9 & 0x02) != 0) state.PressedButtons.Add(DualSenseButton.R1);
        if ((b9 & 0x04) != 0) state.PressedButtons.Add(DualSenseButton.L2);
        if ((b9 & 0x08) != 0) state.PressedButtons.Add(DualSenseButton.R2);
        if ((b9 & 0x10) != 0) state.PressedButtons.Add(DualSenseButton.Create);
        if ((b9 & 0x20) != 0) state.PressedButtons.Add(DualSenseButton.Options);
        if ((b9 & 0x40) != 0) state.PressedButtons.Add(DualSenseButton.L3);
        if ((b9 & 0x80) != 0) state.PressedButtons.Add(DualSenseButton.R3);

        // 5. 시스템 및 마이크 음소거 버튼
        byte b10 = report[offset + 10];
        if ((b10 & 0x01) != 0) state.PressedButtons.Add(DualSenseButton.PlayStation);
        if ((b10 & 0x02) != 0) state.PressedButtons.Add(DualSenseButton.TouchpadClick);
        if ((b10 & 0x04) != 0)
        {
            state.PressedButtons.Add(DualSenseButton.Mute);
            state.IsMicMuted = true;
        }

        // 6. 터치패드 터치 좌표 (USB 기준 33~36바이트)
        if (report.Length >= offset + 37)
        {
            state.Touch1 = ParseTouchPoint(report.Slice(offset + 33, 4));
        }

        // 7. 배터리 상태 (USB 기준 53바이트)
        if (report.Length >= offset + 54)
        {
            byte bat = report[offset + 53];
            int level = bat & 0x0F;
            state.BatteryPercentage = Math.Min(100, level * 10);
            state.IsCharging = (bat & 0x10) != 0;
        }

        return state;
    }

    private static double NormalizeAxis(byte value)
    {
        // 0..255 -> -1.0 .. 1.0 (128 = 0.0)
        return (value - 128.0) / 128.0;
    }

    private static void ParseDpad(byte dpad, HashSet<DualSenseButton> buttons)
    {
        switch (dpad)
        {
            case 0: buttons.Add(DualSenseButton.DpadUp); break;
            case 1: buttons.Add(DualSenseButton.DpadUp); buttons.Add(DualSenseButton.DpadRight); break;
            case 2: buttons.Add(DualSenseButton.DpadRight); break;
            case 3: buttons.Add(DualSenseButton.DpadDown); buttons.Add(DualSenseButton.DpadRight); break;
            case 4: buttons.Add(DualSenseButton.DpadDown); break;
            case 5: buttons.Add(DualSenseButton.DpadDown); buttons.Add(DualSenseButton.DpadLeft); break;
            case 6: buttons.Add(DualSenseButton.DpadLeft); break;
            case 7: buttons.Add(DualSenseButton.DpadUp); buttons.Add(DualSenseButton.DpadLeft); break;
            default: break; // 8 이상은 뗌 상태
        }
    }

    private static DualSenseState.TouchPoint ParseTouchPoint(ReadOnlySpan<byte> slice)
    {
        // Touch byte 0: Bit 7 = active (0 is active in HID), Bits 0-6 = touch id
        bool isActive = (slice[0] & 0x80) == 0;
        int touchId = slice[0] & 0x7F;

        // X: 12-bit (byte 1 + lower 4-bit of byte 2)
        int x = slice[1] | ((slice[2] & 0x0F) << 8);

        // Y: 12-bit (upper 4-bit of byte 2 + byte 3)
        int y = ((slice[2] & 0xF0) >> 4) | (slice[3] << 4);

        return new DualSenseState.TouchPoint(isActive, x, y, touchId);
    }
}
