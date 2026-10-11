using PortKiller.Game.Controller;
using PortKiller.Game.Models;
using Xunit;

namespace PortKiller.Game.Tests;

public sealed class DualSenseHidParserTests
{
    [Fact]
    public void Parse_ShortReport_ReturnsEmptyStateSafely()
    {
        var shortReport = new byte[5];
        var state = DualSenseHidParser.Parse(shortReport);

        Assert.True(state.IsConnected);
        Assert.Empty(state.PressedButtons);
    }

    [Fact]
    public void Parse_NeutralSticks_DecodesZeroAxes()
    {
        var report = new byte[64];
        report[1] = 128; // Left X
        report[2] = 128; // Left Y
        report[3] = 128; // Right X
        report[4] = 128; // Right Y

        var state = DualSenseHidParser.Parse(report);

        Assert.Equal(0.0, state.LeftStickX, precision: 2);
        Assert.Equal(0.0, state.LeftStickY, precision: 2);
        Assert.Equal(0.0, state.RightStickX, precision: 2);
        Assert.Equal(0.0, state.RightStickY, precision: 2);
    }

    [Fact]
    public void Parse_DpadUpAndActionButtons_CorrectlyDecodes()
    {
        var report = new byte[64];
        // byte 8: D-Pad 0 (Up), Square (0x10) | Cross (0x20)
        report[8] = 0x00 | 0x10 | 0x20;

        var state = DualSenseHidParser.Parse(report);

        Assert.Contains(DualSenseButton.DpadUp, state.PressedButtons);
        Assert.Contains(DualSenseButton.Square, state.PressedButtons);
        Assert.Contains(DualSenseButton.Cross, state.PressedButtons);
        Assert.DoesNotContain(DualSenseButton.Circle, state.PressedButtons);
    }

    [Fact]
    public void Parse_BumpersAndTriggers_DecodesCorrectly()
    {
        var report = new byte[64];
        report[5] = 255; // L2 full pressure
        report[6] = 128; // R2 half pressure
        report[9] = 0x01 | 0x02; // L1 and R1 pressed

        var state = DualSenseHidParser.Parse(report);

        Assert.Equal(1.0, state.L2Trigger, precision: 2);
        Assert.Equal(0.5, state.R2Trigger, precision: 1);
        Assert.Contains(DualSenseButton.L1, state.PressedButtons);
        Assert.Contains(DualSenseButton.R1, state.PressedButtons);
    }

    [Fact]
    public void Parse_MicMuteButton_SetsMuteFlag()
    {
        var report = new byte[64];
        report[10] = 0x04; // Mic Mute bit

        var state = DualSenseHidParser.Parse(report);

        Assert.True(state.IsMicMuted);
        Assert.Contains(DualSenseButton.Mute, state.PressedButtons);
    }

    [Fact]
    public void Parse_Battery_DecodesPercentage()
    {
        var report = new byte[64];
        report[53] = 0x08; // Level 8 = 80%

        var state = DualSenseHidParser.Parse(report);

        Assert.Equal(80, state.BatteryPercentage);
    }
}
