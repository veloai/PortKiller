using PortKiller.Core;
using Xunit;

namespace PortKiller.Core.Tests;

public class VoxelAnimalsTests
{
    public static IEnumerable<object[]> AllKinds() =>
        from k in PetKinds.All
        from v in new[] { "day", "evening" }
        select new object[] { k.Id, v };

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Every_box_has_positive_size_and_a_color(string kind, string variant)
    {
        foreach (var b in VoxelAnimals.Boxes(kind, variant))
        {
            Assert.True(b.X1 > b.X0 && b.Y1 > b.Y0 && b.Z1 > b.Z0, $"{kind}/{b.Part} 상자 크기가 0 이하");
            Assert.Matches("^#[0-9A-Fa-f]{6}$", b.Color);
        }
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Model_fits_declared_bounds_and_stands_on_the_ground(string kind, string variant)
    {
        var boxes = VoxelAnimals.Boxes(kind, variant);
        var (minX, maxX, maxY) = VoxelAnimals.Bounds(kind);
        var body = boxes.Where(b => b.Part is not ("Glasses" or "Pointer")).ToList();

        Assert.True(body.Min(b => b.X0) >= minX - 0.01f, $"{kind} 왼쪽이 경계를 넘음");
        Assert.True(body.Max(b => b.X1) <= maxX + 0.01f, $"{kind} 오른쪽이 경계를 넘음");
        Assert.True(body.Max(b => b.Y1) <= maxY + 0.01f, $"{kind} 높이가 경계를 넘음");
        Assert.Equal(0f, body.Min(b => b.Y0));
    }

    [Theory]
    [InlineData(PetKinds.RedPanda)]
    [InlineData(PetKinds.Cat)]
    [InlineData(PetKinds.Fox)]
    public void Quadrupeds_share_the_rig_parts_the_view_animates(string kind)
    {
        var boxes = VoxelAnimals.Boxes(kind, "day");

        foreach (var part in VoxelPanda.Pivots.Keys)
            Assert.Contains(boxes, b => b.Part == part);
        foreach (var part in new[] { VoxelPanda.EyesOpen, VoxelPanda.EyesClosed, VoxelPanda.Glasses, VoxelPanda.Pointer })
            Assert.Contains(boxes, b => b.Part == part);
    }

    [Fact]
    public void Chick_has_the_biped_rig_parts()
    {
        var boxes = VoxelAnimals.Boxes(PetKinds.Chick, "day");

        foreach (var part in VoxelChick.Pivots.Keys)
            Assert.Contains(boxes, b => b.Part == part);
        foreach (var part in new[] { VoxelChick.EyesOpen, VoxelChick.EyesClosed, VoxelChick.Glasses, VoxelChick.Pointer })
            Assert.Contains(boxes, b => b.Part == part);
    }

    [Fact]
    public void Cat_and_fox_look_different_from_each_other_and_from_the_red_panda()
    {
        var colors = new[] { PetKinds.RedPanda, PetKinds.Cat, PetKinds.Fox }
            .Select(k => VoxelAnimals.Boxes(k, "day").First(b => b.Part == "Body").Color)
            .Distinct()
            .Count();

        Assert.Equal(3, colors);
    }

    [Fact]
    public void Briefing_glasses_sit_in_front_of_the_eyes_for_every_kind()
    {
        foreach (var kind in PetKinds.All)
        {
            var boxes = VoxelAnimals.Boxes(kind.Id, "day");
            var eyeFront = boxes.Where(b => b.Part == "EyesOpen").Max(b => b.X1);

            Assert.True(boxes.Where(b => b.Part == "Glasses").Max(b => b.X1) > eyeFront, kind.Id);
        }
    }

    [Fact]
    public void Unknown_kind_falls_back_to_the_red_panda_model()
    {
        Assert.Equal(VoxelPanda.Boxes("day").Count, VoxelAnimals.Boxes("dragon", "day").Count);
    }

    [Theory]
    [InlineData(PetKinds.RedPanda, false)]
    [InlineData(PetKinds.Cat, false)]
    [InlineData(PetKinds.Fox, true)]
    public void Cub_basket_is_valid_stays_in_declared_size_and_awake_differs(string kind, bool awake)
    {
        var cub = VoxelProps.Cub(kind, awake);

        Assert.All(cub, b => Assert.True(b.X1 > b.X0 && b.Y1 > b.Y0 && b.Z1 > b.Z0));
        Assert.Equal(0f, cub.Min(b => b.Y0));
        Assert.True(cub.Max(b => b.Y1) <= VoxelProps.CubHeight + 0.01f);
        Assert.True(cub.Max(b => Math.Max(b.X1, b.Z1)) <= VoxelProps.CubHalfWidth + 0.25f);
        Assert.True(cub.Min(b => Math.Min(b.X0, b.Z0)) >= -VoxelProps.CubHalfWidth - 0.25f);
        Assert.NotEqual(VoxelProps.Cub(kind, !awake).Max(b => b.Y1), cub.Max(b => b.Y1));
    }

    [Fact]
    public void Egg_spot_color_follows_the_species()
    {
        var chickEgg = VoxelProps.Egg(false, VoxelAnimals.EggSpot(PetKinds.Chick));
        var defaultEgg = VoxelProps.Egg(false);

        Assert.Contains(chickEgg, b => b.Color == VoxelAnimals.EggSpot(PetKinds.Chick));
        Assert.DoesNotContain(defaultEgg, b => b.Color == VoxelAnimals.EggSpot(PetKinds.Chick));
    }
}
