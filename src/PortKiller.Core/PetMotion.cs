namespace PortKiller.Core;

/// <summary>펫이 지금 무엇을 하고 있는지. 화면은 이 값으로 그림을 고른다.</summary>
public enum PetActivity
{
    Idle,
    Walk,
    Jump,
    Fall,
    Dragged,
    Sleep,
}

/// <summary>
/// 펫이 돌아다닐 수 있는 범위. 화면 좌표계를 Core 가 알 필요는 없고,
/// "왼쪽 끝, 오른쪽 끝, 바닥 높이" 세 숫자면 충분하다.
/// </summary>
public readonly record struct MotionBounds(double Left, double Right, double GroundY);

/// <summary>
/// 펫의 움직임. 위치와 다음 행동을 여기서 정한다.
///
/// 화면·창·마우스를 모르기 때문에 시간만 흘려보내며 단독으로 시험할 수 있다.
/// 실제로 어디에 그릴지는 App 계층이 이 값을 읽어서 한다.
/// </summary>
public sealed class PetMotion
{
    private readonly Random _rng;
    private readonly double _width;
    private readonly double _height;

    private double _velocityY;
    private double _stateSecondsLeft;
    private int _walkDirection = 1;

    public double X { get; private set; }
    public double Y { get; private set; }
    public bool FacingLeft { get; private set; }
    public PetActivity Activity { get; private set; } = PetActivity.Idle;

    public PetMotion(double width, double height, Random? rng = null)
    {
        _width = width;
        _height = height;
        _rng = rng ?? Random.Shared;
    }

    /// <summary>펫을 바닥 위 특정 위치에 세운다. 앱을 켤 때 한 번 부른다.</summary>
    public void PlaceOnGround(double x, MotionBounds bounds)
    {
        X = Math.Clamp(x, bounds.Left, bounds.Right - _width);
        Y = bounds.GroundY - _height;
        _velocityY = 0;
        EnterIdle();
    }

    // ---------- 집어 옮기기 ----------

    public void BeginDrag()
    {
        Activity = PetActivity.Dragged;
        _velocityY = 0;
    }

    /// <summary>드래그 중 위치를 따라간다. 끌고 있는 동안에는 중력이 멈춘다.</summary>
    public void DragTo(double x, double y, MotionBounds bounds)
    {
        if (Activity != PetActivity.Dragged) return;
        X = Math.Clamp(x, bounds.Left, bounds.Right - _width);
        Y = Math.Min(y, bounds.GroundY - _height);
    }

    /// <summary>손을 놓으면 떨어진다.</summary>
    public void EndDrag()
    {
        if (Activity != PetActivity.Dragged) return;
        Activity = PetActivity.Fall;
        _velocityY = 0;
    }

    // ---------- 시간 진행 ----------

    /// <param name="asleep">자는 중이면 움직이지 않는다. 다만 공중이면 떨어지긴 한다.</param>
    public void Update(TimeSpan delta, MotionBounds bounds, bool asleep = false)
    {
        var dt = delta.TotalSeconds;
        if (dt <= 0) return;

        if (Activity == PetActivity.Dragged) return;

        var floor = bounds.GroundY - _height;
        var airborne = Activity is PetActivity.Jump or PetActivity.Fall;

        if (airborne)
        {
            _velocityY += Balance.Gravity * dt;
            Y += _velocityY * dt;

            if (Y >= floor)
            {
                Y = floor;
                _velocityY = 0;
                EnterIdle();
            }
            return;
        }

        // 땅에 서 있다. 자는 중이면 여기서 끝.
        Y = floor;
        if (asleep)
        {
            Activity = PetActivity.Sleep;
            return;
        }
        if (Activity == PetActivity.Sleep) EnterIdle();

        if (Activity == PetActivity.Walk)
        {
            X += Balance.WalkSpeed * _walkDirection * dt;

            // 끝에 닿으면 방향을 바꾼다. 벽을 뚫고 나가지 않게.
            if (X <= bounds.Left)
            {
                X = bounds.Left;
                TurnAround();
            }
            else if (X >= bounds.Right - _width)
            {
                X = bounds.Right - _width;
                TurnAround();
            }
        }

        _stateSecondsLeft -= dt;
        if (_stateSecondsLeft > 0) return;

        if (Activity == PetActivity.Walk) EnterIdle();
        else ChooseNextFromIdle();
    }

    // ---------- 행동 고르기 ----------

    private void EnterIdle()
    {
        Activity = PetActivity.Idle;
        _stateSecondsLeft = Pick(Balance.IdleSecondsMin, Balance.IdleSecondsMax);
    }

    private void ChooseNextFromIdle()
    {
        if (_rng.NextDouble() < Balance.JumpChance)
        {
            Activity = PetActivity.Jump;
            _velocityY = Balance.JumpVelocity;
            return;
        }

        Activity = PetActivity.Walk;
        _walkDirection = _rng.Next(2) == 0 ? -1 : 1;
        FacingLeft = _walkDirection < 0;
        _stateSecondsLeft = Pick(Balance.WalkSecondsMin, Balance.WalkSecondsMax);
    }

    private void TurnAround()
    {
        _walkDirection = -_walkDirection;
        FacingLeft = _walkDirection < 0;
    }

    private double Pick(double min, double max) => min + _rng.NextDouble() * (max - min);
}
