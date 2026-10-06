using PortKiller.Core;
using Xunit;

namespace PortKiller.Core.Tests;

public class PetKindTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 10, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public void Catalog_has_the_four_launch_species_with_unique_ids_and_names()
    {
        Assert.Equal(new[] { "red_panda", "cat", "fox", "chick" }, PetKinds.All.Select(k => k.Id));
        Assert.Equal(PetKinds.All.Count, PetKinds.All.Select(k => k.Name).Distinct().Count());
    }

    [Fact]
    public void Mammals_are_born_as_cubs_and_the_chick_hatches_from_an_egg()
    {
        Assert.Equal(BirthKind.Cub, PetKinds.Get(PetKinds.RedPanda).Birth);
        Assert.Equal(BirthKind.Cub, PetKinds.Get(PetKinds.Cat).Birth);
        Assert.Equal(BirthKind.Cub, PetKinds.Get(PetKinds.Fox).Birth);
        Assert.Equal(BirthKind.Egg, PetKinds.Get(PetKinds.Chick).Birth);
        Assert.Equal("알", PetKinds.Get(PetKinds.Chick).NewbornNoun);
        Assert.Equal("새끼", PetKinds.Get(PetKinds.Cat).NewbornNoun);
    }

    [Fact]
    public void Chick_uses_the_biped_rig_and_mammals_the_quadruped_rig()
    {
        Assert.Equal(RigKind.Biped, PetKinds.Get(PetKinds.Chick).Rig);
        Assert.All(new[] { PetKinds.RedPanda, PetKinds.Cat, PetKinds.Fox },
            id => Assert.Equal(RigKind.Quadruped, PetKinds.Get(id).Rig));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("dragon")]
    public void Unknown_or_empty_kind_falls_back_without_crashing_and_is_not_chosen(string? id)
    {
        Assert.False(PetKinds.IsChosen(id));
        Assert.Equal(PetKinds.RedPanda, PetKinds.Get(id).Id);
    }

    [Fact]
    public void New_pet_defaults_to_red_panda_and_can_be_another_species()
    {
        Assert.Equal(PetKinds.RedPanda, new Pet(T0).Kind);
        Assert.Equal(PetKinds.Fox, new Pet(T0, kind: PetKinds.Fox).Kind);
    }

    [Fact]
    public void Kind_survives_save_and_load()
    {
        var pet = new Pet(T0, kind: PetKinds.Chick);

        var restored = Pet.FromSave(pet.ToSave(T0));

        Assert.Equal(PetKinds.Chick, restored.Kind);
    }

    [Fact]
    public void Old_version_1_saves_become_red_pandas()
    {
        var old = new SaveData { Version = 1, Kind = "" };

        Assert.Equal(PetKinds.RedPanda, Pet.FromSave(old).Kind);
    }

    [Fact]
    public void A_fresh_version_2_save_has_no_kind_yet_so_the_picker_is_shown()
    {
        var fresh = new SaveData();

        Assert.Equal(2, fresh.Version);
        Assert.Equal("", Pet.FromSave(fresh).Kind);
        Assert.False(PetKinds.IsChosen(Pet.FromSave(fresh).Kind));
    }

    [Fact]
    public void Newborn_texts_differ_between_egg_and_cub_species()
    {
        var chick = PetKinds.Get(PetKinds.Chick);
        var cat = PetKinds.Get(PetKinds.Cat);

        Assert.NotEqual(CareText.NewbornGreeting(chick), CareText.NewbornGreeting(cat));
        Assert.Contains("고양이", CareText.NewbornGreeting(cat));
        Assert.NotEqual(CareText.StillNewborn(chick), CareText.StillNewborn(cat));
        Assert.NotEqual(CareText.Born(chick), CareText.Born(cat));
        Assert.NotEqual(CareText.NewbornHint(chick), CareText.NewbornHint(cat));
        Assert.Equal(CareText.StillNewborn(cat), CareText.For(CareAction.Feed, CareResult.StillAnEgg, null, cat));
    }
}
