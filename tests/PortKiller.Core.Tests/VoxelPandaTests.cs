using PortKiller.Core;
using Xunit;

namespace PortKiller.Core.Tests;

public class VoxelPandaTests
{
    [Theory]
    [InlineData("day")]
    [InlineData("evening")]
    [InlineData("")]
    public void Every_box_has_positive_size(string species)
    {
        foreach (var b in VoxelPanda.Boxes(species))
        {
            Assert.True(b.X1 > b.X0 && b.Y1 > b.Y0 && b.Z1 > b.Z0, $"{b.Part} 상자 크기가 0 이하");
        }
    }

    [Fact]
    public void Feet_touch_the_ground_and_nothing_is_below_it()
    {
        var boxes = VoxelPanda.Boxes("day");

        Assert.Equal(0f, boxes.Min(b => b.Y0));
        Assert.All(new[] { VoxelPanda.LegFL, VoxelPanda.LegFR, VoxelPanda.LegBL, VoxelPanda.LegBR },
            leg => Assert.Equal(0f, boxes.Where(b => b.Part == leg).Min(b => b.Y0)));
    }

    [Fact]
    public void Model_fits_the_declared_bounds()
    {
        var boxes = VoxelPanda.Boxes("day");

        Assert.True(boxes.Min(b => b.X0) >= VoxelPanda.MinX);
        Assert.True(boxes.Max(b => b.X1) <= VoxelPanda.MaxX);
        Assert.True(boxes.Max(b => b.Y1) <= VoxelPanda.MaxY);
    }

    [Fact]
    public void Every_animated_part_has_a_pivot_and_a_box()
    {
        var boxes = VoxelPanda.Boxes("day");

        foreach (var part in VoxelPanda.Pivots.Keys)
            Assert.Contains(boxes, b => b.Part == part);
    }

    [Fact]
    public void Both_eye_states_exist_and_species_differ_in_color()
    {
        var day = VoxelPanda.Boxes("day");
        var eve = VoxelPanda.Boxes("evening");

        Assert.Contains(day, b => b.Part == VoxelPanda.EyesOpen);
        Assert.Contains(day, b => b.Part == VoxelPanda.EyesClosed);
        Assert.NotEqual(day[0].Color, eve[0].Color);
    }
}
