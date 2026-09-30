using PortKiller.Core;
using Xunit;

namespace PortKiller.Core.Tests;

public class VoxelPropsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Egg_boxes_are_valid_stand_on_the_ground_and_fit_declared_size(bool cracked)
    {
        var egg = VoxelProps.Egg(cracked);

        Assert.All(egg, b => Assert.True(b.X1 > b.X0 && b.Y1 > b.Y0 && b.Z1 > b.Z0));
        Assert.Equal(0f, egg.Min(b => b.Y0));
        Assert.Equal(VoxelProps.EggHeight, egg.Max(b => b.Y1));
        Assert.True(egg.Max(b => Math.Max(b.X1, b.Z1)) <= VoxelProps.EggHalfWidth + 0.01f);
        Assert.True(egg.Min(b => Math.Min(b.X0, b.Z0)) >= -VoxelProps.EggHalfWidth - 0.01f);
    }

    [Fact]
    public void Cracked_egg_has_more_pieces_than_the_plain_one()
    {
        Assert.True(VoxelProps.Egg(true).Count > VoxelProps.Egg(false).Count);
    }

    [Fact]
    public void Poop_is_valid_and_fits_declared_size()
    {
        var poop = VoxelProps.Poop();

        Assert.All(poop, b => Assert.True(b.X1 > b.X0 && b.Y1 > b.Y0 && b.Z1 > b.Z0));
        Assert.Equal(0f, poop.Min(b => b.Y0));
        Assert.Equal(VoxelProps.PoopHeight, poop.Max(b => b.Y1));
        Assert.True(poop.Max(b => Math.Max(b.X1, b.Z1)) <= VoxelProps.PoopHalfWidth + 0.21f);
    }
}
