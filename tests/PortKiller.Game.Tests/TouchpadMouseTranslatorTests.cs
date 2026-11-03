using PortKiller.Game.Controller;
using PortKiller.Game.Models;
using Xunit;

namespace PortKiller.Game.Tests;

public sealed class TouchpadMouseTranslatorTests
{
    [Fact]
    public void ProcessTouch_FirstTouchDown_DoesNotJumpCursor()
    {
        var translator = new TouchpadMouseTranslator();
        var touch = new DualSenseState.TouchPoint(IsActive: true, X: 500, Y: 300, TouchId: 1);

        var (dx, dy) = translator.ProcessTouch(touch, sensitivity: 8.0);

        Assert.Equal(0, dx);
        Assert.Equal(0, dy);
    }

    [Fact]
    public void ProcessTouch_DragMovement_ComputesSmoothDelta()
    {
        var translator = new TouchpadMouseTranslator();

        // 1. Initial touch
        var initial = new DualSenseState.TouchPoint(IsActive: true, X: 500, Y: 300, TouchId: 1);
        translator.ProcessTouch(initial, sensitivity: 8.0);

        // 2. Drag by +20, +10
        var moved = new DualSenseState.TouchPoint(IsActive: true, X: 520, Y: 310, TouchId: 1);
        var (dx, dy) = translator.ProcessTouch(moved, sensitivity: 8.0);

        Assert.True(dx > 0);
        Assert.True(dy > 0);
    }

    [Fact]
    public void ProcessTouch_InactiveTouch_ResetsDelta()
    {
        var translator = new TouchpadMouseTranslator();

        var inactive = new DualSenseState.TouchPoint(IsActive: false, X: 0, Y: 0, TouchId: 0);
        var (dx, dy) = translator.ProcessTouch(inactive, sensitivity: 8.0);

        Assert.Equal(0, dx);
        Assert.Equal(0, dy);
    }
}
