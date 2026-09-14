using PortKiller.Core;
using Xunit;

namespace PortKiller.Core.Tests;

public class PetMotionTests
{
    private static readonly MotionBounds Screen = new(0, 1920, 1000);
    private const double W = 96, H = 96;

    private static PetMotion Standing(int seed = 1)
    {
        var m = new PetMotion(W, H, new Random(seed));
        m.PlaceOnGround(900, Screen);
        return m;
    }

    [Fact]
    public void PlaceOnGround_PutsFeetOnTheFloor()
    {
        var m = Standing();
        Assert.Equal(Screen.GroundY - H, m.Y, precision: 6);
        Assert.Equal(PetActivity.Idle, m.Activity);
    }

    /// <summary>오래 굴려도 화면 밖으로 나가면 안 된다.</summary>
    [Fact]
    public void NeverLeavesTheScreen()
    {
        var m = Standing();
        for (var i = 0; i < 20000; i++)
        {
            m.Update(TimeSpan.FromMilliseconds(16), Screen);
            Assert.InRange(m.X, Screen.Left, Screen.Right - W);
            Assert.True(m.Y <= Screen.GroundY - H + 0.001, $"바닥을 뚫었다: Y={m.Y}");
        }
    }

    /// <summary>가만히 두면 언젠가는 걷기 시작한다.</summary>
    [Fact]
    public void EventuallyStartsWalking()
    {
        var m = Standing();
        var walked = false;
        var startX = m.X;

        for (var i = 0; i < 2000 && !walked; i++)
        {
            m.Update(TimeSpan.FromMilliseconds(16), Screen);
            if (m.Activity == PetActivity.Walk && Math.Abs(m.X - startX) > 1) walked = true;
        }

        Assert.True(walked, "30초를 굴려도 한 발짝도 걷지 않았다");
    }

    /// <summary>자는 동안에는 제자리에 있어야 한다.</summary>
    [Fact]
    public void DoesNotWanderWhileAsleep()
    {
        var m = Standing();
        var x = m.X;

        for (var i = 0; i < 2000; i++) m.Update(TimeSpan.FromMilliseconds(16), Screen, asleep: true);

        Assert.Equal(x, m.X, precision: 6);
        Assert.Equal(PetActivity.Sleep, m.Activity);
    }

    [Fact]
    public void DragMovesPetAndStopsGravity()
    {
        var m = Standing();
        m.BeginDrag();
        m.DragTo(300, 200, Screen);

        // 끌고 있는 동안에는 시간이 흘러도 떨어지지 않는다.
        for (var i = 0; i < 100; i++) m.Update(TimeSpan.FromMilliseconds(16), Screen);

        Assert.Equal(PetActivity.Dragged, m.Activity);
        Assert.Equal(300, m.X, precision: 6);
        Assert.Equal(200, m.Y, precision: 6);
    }

    [Fact]
    public void ReleasedInTheAir_FallsToTheGround()
    {
        var m = Standing();
        m.BeginDrag();
        m.DragTo(300, 100, Screen);
        m.EndDrag();

        Assert.Equal(PetActivity.Fall, m.Activity);

        for (var i = 0; i < 300; i++) m.Update(TimeSpan.FromMilliseconds(16), Screen);

        Assert.Equal(Screen.GroundY - H, m.Y, precision: 6);
        Assert.NotEqual(PetActivity.Fall, m.Activity);
    }

    /// <summary>드래그 중에도 화면 밖으로는 못 나간다.</summary>
    [Fact]
    public void DragIsClampedToScreen()
    {
        var m = Standing();
        m.BeginDrag();

        m.DragTo(-500, 500, Screen);
        Assert.Equal(Screen.Left, m.X, precision: 6);

        m.DragTo(99999, 500, Screen);
        Assert.Equal(Screen.Right - W, m.X, precision: 6);
    }
}
