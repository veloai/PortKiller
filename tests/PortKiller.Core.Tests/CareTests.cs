using PortKiller.Core;
using Xunit;

namespace PortKiller.Core.Tests;

public class CareTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 14, 9, 0, 0, TimeSpan.FromHours(9));

    private static Pet Hatched(int seed = 3)
    {
        var pet = new Pet(T0, new Random(seed));
        for (var i = 0; i < Balance.HatchClicksRequired; i++) pet.RegisterEggClick(T0);
        return pet;
    }

    // ---------- 대사가 빠진 경우가 없어야 한다 ----------

    /// <summary>
    /// 모든 돌보기 × 모든 결과 조합에 말이 있어야 한다.
    /// 하나라도 비면 화면에 빈 말풍선이 뜬다.
    /// </summary>
    [Fact]
    public void EveryActionAndResultHasSomethingToSay()
    {
        var rng = new Random(1);
        foreach (var action in Enum.GetValues<CareAction>())
        foreach (var result in Enum.GetValues<CareResult>())
        {
            var line = CareText.For(action, result, rng);
            Assert.False(string.IsNullOrWhiteSpace(line), $"{action}/{result} 에 대사가 없다");
        }
    }

    [Fact]
    public void GreetingAndStageLinesExist()
    {
        foreach (var stage in Enum.GetValues<LifeStage>())
        {
            Assert.False(string.IsNullOrWhiteSpace(CareText.Greeting(stage, new Random(1))));
            Assert.False(string.IsNullOrWhiteSpace(CareText.StageUp(stage)));
        }

        Assert.False(string.IsNullOrWhiteSpace(CareText.Evolved(1)));
        Assert.False(string.IsNullOrWhiteSpace(CareText.Evolved(2)));
    }

    // ---------- 거절 규칙 ----------

    [Fact]
    public void EggRefusesEverything()
    {
        var pet = new Pet(T0, new Random(1));

        foreach (var action in Enum.GetValues<CareAction>())
            Assert.Equal(CareResult.StillAnEgg, pet.ApplyCare(action, T0));
    }

    [Fact]
    public void MedicineOnlyWhenSick()
    {
        var pet = Hatched();
        Assert.Equal(CareResult.NotSick, pet.ApplyCare(CareAction.Medicine, T0));
    }

    [Fact]
    public void CleaningRefusedWhenNothingToClean()
    {
        var pet = Hatched();
        Assert.Equal(CareResult.AlreadyClean, pet.ApplyCare(CareAction.Clean, T0));
    }

    [Fact]
    public void CleaningRemovesOnePoop()
    {
        var pet = Hatched();
        pet.Tick(TimeSpan.FromSeconds(1), T0);
        pet.Tick(TimeSpan.FromMinutes(1), pet.NextPoopAt!.Value.AddSeconds(1));
        Assert.Equal(1, pet.PoopsOnGround);

        Assert.Equal(CareResult.Done, pet.ApplyCare(CareAction.Clean, T0));
        Assert.Equal(0, pet.PoopsOnGround);
    }

    [Fact]
    public void SleepingTwiceIsRefused()
    {
        var pet = Hatched();
        Assert.Equal(CareResult.Done, pet.ApplyCare(CareAction.Sleep, T0));
        Assert.Equal(CareResult.AlreadyAsleep, pet.ApplyCare(CareAction.Sleep, T0));
    }

    [Fact]
    public void PlayingWakesThePetUp()
    {
        var pet = Hatched();
        pet.ApplyCare(CareAction.Sleep, T0);
        Assert.True(pet.IsAsleep);

        Assert.Equal(CareResult.Done, pet.ApplyCare(CareAction.Play, T0));
        Assert.False(pet.IsAsleep);
    }

    /// <summary>재운 뒤 영영 자고 있으면 안 된다. 기운이 다 차면 스스로 깬다.</summary>
    [Fact]
    public void WakesUpOnceFullyRested()
    {
        var pet = Hatched();
        pet.ApplyCare(CareAction.Sleep, T0);

        pet.Tick(TimeSpan.FromHours(10), T0.AddHours(10));

        Assert.False(pet.IsAsleep);
    }

    /// <summary>거절당했으면 스탯이 하나도 안 움직여야 한다.</summary>
    [Fact]
    public void RefusedCareChangesNothing()
    {
        var pet = Hatched();
        var beforeCount = pet.CareCount;
        var beforeClean = pet.Stats.Cleanliness;
        var beforeHealth = pet.Stats.Health;

        Assert.NotEqual(CareResult.Done, pet.ApplyCare(CareAction.Clean, T0));
        Assert.NotEqual(CareResult.Done, pet.ApplyCare(CareAction.Medicine, T0));

        Assert.Equal(beforeCount, pet.CareCount);
        Assert.Equal(beforeClean, pet.Stats.Cleanliness, precision: 6);
        Assert.Equal(beforeHealth, pet.Stats.Health, precision: 6);
    }
}
