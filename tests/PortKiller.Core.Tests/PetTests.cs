using PortKiller.Core;
using Xunit;

namespace PortKiller.Core.Tests;

public class PetTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 14, 9, 0, 0, TimeSpan.FromHours(9));

    /// <summary>알에서 깨어나기 전에는 시간이 지나도 스탯이 깎이지 않아야 한다.</summary>
    [Fact]
    public void Egg_DoesNotDecay()
    {
        var pet = new Pet(T0);
        var before = pet.Stats.Hunger;

        pet.Tick(TimeSpan.FromHours(1), T0);

        Assert.Equal(LifeStage.Egg, pet.Stage);
        Assert.Equal(before, pet.Stats.Hunger);
    }

    [Fact]
    public void Egg_HatchesAfterMaxWait()
    {
        var pet = new Pet(T0);

        pet.Tick(TimeSpan.FromMinutes(1), pet.EggCreatedAt.AddHours(Balance.HatchHoursMax));

        Assert.Equal(LifeStage.Baby, pet.Stage);
        Assert.NotNull(pet.BornAt);
        Assert.NotEqual("", pet.SpeciesId);
    }

    [Fact]
    public void Egg_HatchesByClicking()
    {
        var pet = new Pet(T0);

        for (var i = 0; i < Balance.HatchClicksRequired; i++)
            pet.RegisterEggClick(T0);

        Assert.Equal(LifeStage.Baby, pet.Stage);
    }

    [Fact]
    public void Hunger_DecaysByTheHourlyRate()
    {
        var pet = Hatched();
        var before = pet.Stats.Hunger;

        pet.Tick(TimeSpan.FromHours(1), T0.AddHours(1));

        Assert.Equal(before - Balance.HungerDecayPerHour, pet.Stats.Hunger, precision: 6);
    }

    /// <summary>앱을 꺼둔 시간은 절반만 인정한다.</summary>
    [Fact]
    public void OfflineTime_CountsHalf()
    {
        var online = Hatched();
        var offline = Hatched();

        online.Tick(TimeSpan.FromHours(2), T0.AddHours(2));
        offline.CatchUpOffline(TimeSpan.FromHours(2), T0.AddHours(2));

        var onlineLost = 80 - online.Stats.Hunger;
        var offlineLost = 80 - offline.Stats.Hunger;
        Assert.Equal(onlineLost * Balance.OfflineDecayRate, offlineLost, precision: 6);
    }

    /// <summary>며칠을 꺼놔도 8시간치 넘게는 깎이지 않는다. 죽지 않게 하는 장치.</summary>
    [Fact]
    public void OfflineTime_IsCapped()
    {
        var aDay = Hatched();
        var aWeek = Hatched();

        aDay.CatchUpOffline(TimeSpan.FromDays(1), T0.AddDays(1));
        aWeek.CatchUpOffline(TimeSpan.FromDays(7), T0.AddDays(7));

        Assert.Equal(aDay.Stats.Hunger, aWeek.Stats.Hunger, precision: 6);
    }

    [Fact]
    public void Stats_NeverLeaveZeroToHundred()
    {
        var pet = Hatched();

        pet.CatchUpOffline(TimeSpan.FromDays(30), T0.AddDays(30));
        Assert.InRange(pet.Stats.Hunger, 0, 100);

        for (var i = 0; i < 50; i++) pet.ApplyCare(CareAction.Feed, T0);
        Assert.InRange(pet.Stats.Hunger, 0, 100);
    }

    [Fact]
    public void Medicine_OnlyWorksWhenSick()
    {
        var pet = Hatched();

        Assert.Equal(CareResult.NotSick, pet.ApplyCare(CareAction.Medicine, T0));
    }

    [Fact]
    public void Growth_FollowsRealElapsedDays()
    {
        var pet = Hatched();

        pet.Tick(TimeSpan.FromMinutes(1), pet.BornAt!.Value.AddDays(Balance.StageChildDays));
        Assert.Equal(LifeStage.Child, pet.Stage);

        pet.Tick(TimeSpan.FromMinutes(1), pet.BornAt!.Value.AddDays(Balance.StageAdultDays));
        Assert.Equal(LifeStage.Adult, pet.Stage);
    }

    /// <summary>진화는 "집중한 시간"과 "서로 다른 날"로만 판정한다. 키 횟수는 쓰지 않는다.</summary>
    [Fact]
    public void Evolution_NeedsFocusHoursAndDistinctDays()
    {
        var pet = Hatched();
        pet.Tick(TimeSpan.FromMinutes(1), pet.BornAt!.Value.AddDays(Balance.StageAdultDays));
        Assert.Equal(LifeStage.Adult, pet.Stage);

        // 시간만 채우고 날짜가 모자라면 진화하지 않는다.
        pet.AddFocusTime(TimeSpan.FromHours(Balance.EvolutionFocusHours), T0);
        Assert.Equal(0, pet.EvolutionTier);

        for (var d = 1; d < Balance.EvolutionDistinctDays; d++)
            pet.AddFocusTime(TimeSpan.FromMinutes(1), T0.AddDays(d));

        Assert.Equal(1, pet.EvolutionTier);
    }

    [Fact]
    public void SaveAndLoad_KeepsEverything()
    {
        var pet = Hatched();
        pet.ApplyCare(CareAction.Feed, T0);
        pet.AddFocusTime(TimeSpan.FromHours(3), T0);

        var restored = Pet.FromSave(pet.ToSave(T0));

        Assert.Equal(pet.Stage, restored.Stage);
        Assert.Equal(pet.SpeciesId, restored.SpeciesId);
        Assert.Equal(pet.Stats.Hunger, restored.Stats.Hunger, precision: 6);
        Assert.Equal(pet.FocusSeconds, restored.FocusSeconds, precision: 6);
        Assert.Equal(pet.DistinctActiveDays, restored.DistinctActiveDays);
    }

    private static Pet Hatched()
    {
        var pet = new Pet(T0);
        for (var i = 0; i < Balance.HatchClicksRequired; i++) pet.RegisterEggClick(T0);
        return pet;
    }
}
