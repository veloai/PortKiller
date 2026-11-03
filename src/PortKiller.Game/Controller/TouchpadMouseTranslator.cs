using PortKiller.Game.Models;

namespace PortKiller.Game.Controller;

/// <summary>
/// 듀얼센스의 터치패드 좌표 변화를 마우스 커서 델타(dx, dy)로 변환하는 변환기.
/// 
/// 터치 시작 시에는 점프를 방지하고, 손가락이 움직일 때만 부드럽게 마우스 상대 이동을 생성합니다.
/// </summary>
public sealed class TouchpadMouseTranslator
{
    private int _lastX = -1;
    private int _lastY = -1;
    private bool _wasActive = false;

    /// <summary>
    /// 터치패드 상태로부터 마우스 델타 이동량 (dx, dy)을 계산합니다.
    /// </summary>
    public (int dx, int dy) ProcessTouch(DualSenseState.TouchPoint touch, double sensitivity)
    {
        if (!touch.IsActive)
        {
            _wasActive = false;
            _lastX = -1;
            _lastY = -1;
            return (0, 0);
        }

        if (!_wasActive || _lastX < 0 || _lastY < 0)
        {
            // 터치 시작 시점에는 기준점만 잡고 커서를 이동시키지 않음
            _wasActive = true;
            _lastX = touch.X;
            _lastY = touch.Y;
            return (0, 0);
        }

        int rawDx = touch.X - _lastX;
        int rawDy = touch.Y - _lastY;

        _lastX = touch.X;
        _lastY = touch.Y;

        // 너무 큰 튐(글리치) 방지
        if (Math.Abs(rawDx) > 300 || Math.Abs(rawDy) > 300)
        {
            return (0, 0);
        }

        // 감도 계수 적용 (기본 0.05 ~ 0.20 배)
        double factor = (sensitivity / 10.0) * 0.12;
        int dx = (int)Math.Round(rawDx * factor);
        int dy = (int)Math.Round(rawDy * factor);

        return (dx, dy);
    }

    public void Reset()
    {
        _wasActive = false;
        _lastX = -1;
        _lastY = -1;
    }
}
