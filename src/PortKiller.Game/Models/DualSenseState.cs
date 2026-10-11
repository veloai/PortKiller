namespace PortKiller.Game.Models;

/// <summary>
/// PS5 듀얼센스의 실시간 물리 상태 스냅샷
/// </summary>
public sealed class DualSenseState
{
    public bool IsConnected { get; set; }

    // 스틱 좌표 (-1.0 ~ 1.0, 중립 0.0)
    public double LeftStickX { get; set; }
    public double LeftStickY { get; set; }
    public double RightStickX { get; set; }
    public double RightStickY { get; set; }

    // 아날로그 트리거 압력 (0.0 ~ 1.0)
    public double L2Trigger { get; set; }
    public double R2Trigger { get; set; }

    // 눌린 버튼 목록
    public HashSet<DualSenseButton> PressedButtons { get; set; } = new();

    // 터치패드 터치 1 & 2 상태
    public TouchPoint Touch1 { get; set; }
    public TouchPoint Touch2 { get; set; }

    // 하드웨어 상태
    public int BatteryPercentage { get; set; } = 100;
    public bool IsCharging { get; set; }
    public bool IsMicMuted { get; set; }

    public bool IsPressed(DualSenseButton button) => PressedButtons.Contains(button);

    public readonly record struct TouchPoint(bool IsActive, int X, int Y, int TouchId);
}
