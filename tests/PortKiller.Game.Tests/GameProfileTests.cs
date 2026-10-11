using PortKiller.Game.Models;
using PortKiller.Game.Profiles;
using Xunit;

namespace PortKiller.Game.Tests;

public sealed class GameProfileTests
{
    [Fact]
    public void CreateWoWClassicProfile_HasCoreBindingsAndCommands()
    {
        var profile = DefaultProfiles.CreateWoWClassicProfile();

        Assert.Equal("World of Warcraft Classic", profile.Name);
        Assert.Equal("WowClassic", profile.TargetProcessName);

        // Core buttons
        Assert.True(profile.Bindings.ContainsKey(DualSenseButton.Cross));
        Assert.Equal((ushort)0x20, profile.Bindings[DualSenseButton.Cross].NormalKey); // Space

        Assert.True(profile.Bindings.ContainsKey(DualSenseButton.Circle));
        Assert.Equal((ushort)0x46, profile.Bindings[DualSenseButton.Circle].NormalKey); // F

        // Modifiers for Square (1, 5, 6)
        var square = profile.Bindings[DualSenseButton.Square];
        Assert.Equal((ushort)0x31, square.NormalKey);
        Assert.Equal((ushort)0x35, square.L2ModifierKey);
        Assert.Equal((ushort)0x36, square.R2ModifierKey);

        // Voice commands
        Assert.Contains(profile.VoiceCommands, c => c.Phrase == "물약");
        Assert.Contains(profile.VoiceCommands, c => c.Phrase == "전투 태세");
        Assert.Contains(profile.VoiceCommands, c => c.Phrase == "도발");
        Assert.Contains(profile.VoiceCommands, c => c.Phrase == "지도");

        // Mic PTT should default to Mute button
        Assert.True(profile.IsPushToTalk);
        Assert.Equal(DualSenseButton.Mute, profile.PushToTalkButton);
    }
}
