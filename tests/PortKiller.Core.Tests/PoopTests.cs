using PortKiller.Core;
using Xunit;

namespace PortKiller.Core.Tests;

public class PoopTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 14, 9, 0, 0, TimeSpan.FromHours(9));

    private static Pet Hatched(int seed = 7)
    {
        var pet = new Pet(T0, new Random(seed));
        for (var i = 0; i < Balance.HatchClicksRequired; i++) pet.RegisterEggClick(T0);
        return pet;
    }

    [Fact]
    public void FreshPet_HasNoPoopYet()
    {
        var pet = Hatched();
        pet.Tick(TimeSpan.FromMinutes(1), T0.AddMinutes(1));

        Assert.Equal(0, pet.PoopsOnGround);
        Assert.NotNull(pet.NextPoopAt);
    }

    [Fact]
    public void PoopsAfterTheScheduledTime()
    {
        var pet = Hatched();
        pet.Tick(TimeSpan.FromSeconds(1), T0);           // 예정 시각이 잡힌다
        var due = pet.NextPoopAt!.Value;

        pet.Tick(TimeSpan.FromMinutes(1), due.AddSeconds(1));

        Assert.Equal(1, pet.PoopsOnGround);
    }

    /// <summary>켜져 있는 동안에는 한 번에 하나씩만 나온다. 갑자기 우르르 쏟아지지 않게.</summary>
    [Fact]
    public void OnlyOnePoopPerTickWhileRunning()
    {
        var pet = Hatched();
        pet.Tick(TimeSpan.FromSeconds(1), T0);

        pet.Tick(TimeSpan.FromDays(1), T0.AddDays(1));

        Assert.Equal(1, pet.PoopsOnGround);
    }

    /// <summary>꺼둔 사이에 쌓이더라도 정해진 개수까지만.</summary>
    [Fact]
    public void OfflineCatchUp_IsLimited()
    {
        var pet = Hatched();
        pet.Tick(TimeSpan.FromSeconds(1), T0);

        pet.CatchUpOffline(TimeSpan.FromDays(3), T0.AddDays(3));

        Assert.InRange(pet.PoopsOnGround, 1, Balance.MaxOfflinePoops);
    }

    [Fact]
    public void NeverExceedsGroundLimit()
    {
        var pet = Hatched();
        pet.Tick(TimeSpan.FromSeconds(1), T0);

        var now = T0;
        for (var i = 0; i < 200; i++)
        {
            now = now.AddHours(6);
            pet.Tick(TimeSpan.FromHours(6), now);
        }

        Assert.InRange(pet.PoopsOnGround, 0, Balance.MaxPoopsOnGround);
    }

    [Fact]
    public void CleaningRemovesOneAndRaisesCleanliness()
    {
        var pet = Hatched();
        pet.Tick(TimeSpan.FromSeconds(1), T0);
        pet.Tick(TimeSpan.FromMinutes(1), pet.NextPoopAt!.Value.AddSeconds(1));
        Assert.Equal(1, pet.PoopsOnGround);

        var before = pet.Stats.Cleanliness;
        Assert.True(pet.CleanOnePoop());

        Assert.Equal(0, pet.PoopsOnGround);
        Assert.True(pet.Stats.Cleanliness > before);
        Assert.False(pet.CleanOnePoop());
    }

    /// <summary>먹이면 소화 시간 뒤로 응아가 앞당겨진다.</summary>
    [Fact]
    public void FeedingBringsPoopForward()
    {
        var pet = Hatched();
        pet.Tick(TimeSpan.FromSeconds(1), T0);
        var before = pet.NextPoopAt!.Value;

        pet.ApplyCare(CareAction.Feed, T0);

        Assert.True(pet.NextPoopAt!.Value <= before, "먹였는데 응아가 더 늦어졌다");
    }

    /// <summary>
    /// 더러워지는 속도는 "몇 번 불렸나"가 아니라 "얼마나 지났나"에만 달려야 한다.
    ///
    /// 화면은 초당 60번 Tick 을 부른다. 호출 횟수에 비례하게 짜면 청결도가 몇 초 만에 0이 된다.
    /// 실제로 그 버그가 있었고, 화면에 "청결 0" 이 떠서 발견했다. 이 시험이 그 재발을 막는다.
    /// </summary>
    [Fact]
    public void DirtinessDependsOnElapsedTimeNotTickCount()
    {
        var coarse = Hatched();
        var fine = Hatched();
        foreach (var pet in new[] { coarse, fine })
        {
            pet.Tick(TimeSpan.FromSeconds(1), T0);
            pet.Tick(TimeSpan.FromMinutes(1), pet.NextPoopAt!.Value.AddSeconds(1));
        }

        var start = coarse.NextPoopAt!.Value;

        // 한 시간을 한 번에 흘려보낸 쪽
        coarse.Tick(TimeSpan.FromHours(1), start.AddHours(1));

        // 같은 한 시간을 60프레임으로 3600번 나눠 흘려보낸 쪽
        var now = start;
        for (var i = 0; i < 3600; i++)
        {
            now = now.AddSeconds(1);
            fine.Tick(TimeSpan.FromSeconds(1), now);
        }

        Assert.Equal(coarse.Stats.Cleanliness, fine.Stats.Cleanliness, precision: 6);
    }

    [Fact]
    public void PoopSurvivesSaveAndLoad()
    {
        var pet = Hatched();
        pet.Tick(TimeSpan.FromSeconds(1), T0);
        pet.Tick(TimeSpan.FromMinutes(1), pet.NextPoopAt!.Value.AddSeconds(1));

        var restored = Pet.FromSave(pet.ToSave(T0));

        Assert.Equal(pet.PoopsOnGround, restored.PoopsOnGround);
        Assert.Equal(pet.NextPoopAt, restored.NextPoopAt);
    }
}
