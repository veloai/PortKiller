namespace PortKiller.Core;

/// <summary>
/// 펫의 두뇌. 시간이 흐르면 무슨 일이 벌어지는지를 전부 여기서 결정한다.
/// 화면·윈도우·파일에 대해 아무것도 모른다. 그래서 단독으로 테스트된다.
/// </summary>
public sealed class Pet
{
    private readonly HashSet<string> _activeDays = new();

    /// <param name="createdAt">
    /// 알이 생긴 시각. 테스트가 시계에 흔들리지 않도록 바깥에서 넣을 수 있게 열어둔다.
    /// </param>
    public Pet(DateTimeOffset? createdAt = null)
        => EggCreatedAt = createdAt ?? DateTimeOffset.Now;

    public PetStats Stats { get; } = new();
    public LifeStage Stage { get; private set; } = LifeStage.Egg;
    public int EvolutionTier { get; private set; }
    public string SpeciesId { get; private set; } = "";
    public DateTimeOffset? BornAt { get; private set; }

    /// <summary>알이 생긴 시각. 부화 시간 계산의 기준점.</summary>
    public DateTimeOffset EggCreatedAt { get; private set; }

    public int HatchClicks { get; private set; }
    public bool IsSick { get; private set; }
    public bool IsSulking { get; private set; }
    public bool IsAsleep { get; set; }

    public double FocusSeconds { get; private set; }
    public int CareCount { get; private set; }
    public int DistinctActiveDays => _activeDays.Count;

    public event Action<LifeStage>? StageChanged;
    public event Action<int>? Evolved;
    public event Action<bool>? SickChanged;

    // ---------- 시간 진행 ----------

    /// <summary>앱이 켜져 있는 동안 주기적으로 부른다.</summary>
    public void Tick(TimeSpan elapsed, DateTimeOffset now)
        => Advance(elapsed, now, offline: false);

    /// <summary>
    /// 앱이 꺼져 있던 시간을 한 번에 따라잡는다.
    /// 절반만 인정하고 최대 8시간까지만 깎아서, 오래 꺼놔도 죽지 않게 한다.
    /// </summary>
    public void CatchUpOffline(TimeSpan awayFor, DateTimeOffset now)
    {
        if (awayFor <= TimeSpan.Zero) return;

        var cap = TimeSpan.FromHours(Balance.OfflineCapHours);
        var capped = awayFor > cap ? cap : awayFor;
        Advance(capped, now, offline: true);
    }

    private void Advance(TimeSpan elapsed, DateTimeOffset now, bool offline)
    {
        if (elapsed <= TimeSpan.Zero) return;

        if (Stage == LifeStage.Egg)
        {
            TryHatch(now);
            return;
        }

        var hours = elapsed.TotalHours * (offline ? Balance.OfflineDecayRate : 1.0);

        Stats.Hunger -= Balance.HungerDecayPerHour * hours;
        Stats.Happiness -= Balance.HappinessDecayPerHour * hours;
        Stats.Cleanliness -= Balance.CleanlinessDecayPerHour * hours;

        if (IsAsleep)
            Stats.Energy += Balance.SleepEnergyRecoveryPerHour * hours;
        else
            Stats.Energy -= Balance.EnergyDecayPerHour * hours;

        ApplyHealth(hours);
        RefreshConditions();
        TryGrow(now);
    }

    private void ApplyHealth(double hours)
    {
        var drain = 0.0;
        if (Stats.Hunger < Balance.HungerWarnThreshold) drain += Balance.HealthDrainHungryPerHour;
        if (Stats.Cleanliness < Balance.HungerWarnThreshold) drain += Balance.HealthDrainDirtyPerHour;

        if (drain > 0)
            Stats.Health -= drain * hours;
        else
            Stats.Health += Balance.HealthRegenPerHour * hours;
    }

    private void RefreshConditions()
    {
        var wasSick = IsSick;
        if (IsSick && Stats.Health >= Balance.SickRecoverThreshold) IsSick = false;
        else if (!IsSick && Stats.Health < Balance.SickThreshold) IsSick = true;
        if (wasSick != IsSick) SickChanged?.Invoke(IsSick);

        if (IsSulking && Stats.Happiness >= Balance.SulkRecoverThreshold) IsSulking = false;
        else if (!IsSulking && Stats.Happiness < Balance.SulkThreshold) IsSulking = true;
    }

    // ---------- 부화와 성장 ----------

    public void RegisterEggClick(DateTimeOffset now)
    {
        if (Stage != LifeStage.Egg) return;
        HatchClicks++;
        TryHatch(now);
    }

    private void TryHatch(DateTimeOffset now)
    {
        if (Stage != LifeStage.Egg) return;

        var waitedHours = (now - EggCreatedAt).TotalHours;
        var ready = HatchClicks >= Balance.HatchClicksRequired
                    || waitedHours >= Balance.HatchHoursMax;
        if (!ready) return;

        BornAt = now;
        SpeciesId = SpeciesPicker.Pick(now);
        SetStage(LifeStage.Baby);
    }

    private void TryGrow(DateTimeOffset now)
    {
        if (BornAt is null) return;

        var days = (now - BornAt.Value).TotalDays;
        if (Stage == LifeStage.Baby && days >= Balance.StageChildDays) SetStage(LifeStage.Child);
        if (Stage == LifeStage.Child && days >= Balance.StageAdultDays) SetStage(LifeStage.Adult);
    }

    private void SetStage(LifeStage next)
    {
        if (Stage == next) return;
        Stage = next;
        StageChanged?.Invoke(next);
    }

    // ---------- 돌보기 ----------

    /// <summary>돌보기를 적용한다. 지금 할 수 없는 행동이면 false 를 준다.</summary>
    public bool ApplyCare(CareAction action, DateTimeOffset now)
    {
        if (Stage == LifeStage.Egg) return false;

        switch (action)
        {
            case CareAction.Feed:
                Stats.Hunger += 30;
                break;
            case CareAction.Snack:
                Stats.Hunger += 10;
                Stats.Happiness += 5;
                break;
            case CareAction.Play:
                if (IsSick) return false;
                Stats.Happiness += 20;
                Stats.Energy -= 10;
                break;
            case CareAction.Medicine:
                if (!IsSick) return false;
                Stats.Health += 40;
                break;
            case CareAction.Clean:
                Stats.Cleanliness += 15;
                break;
            case CareAction.Pet:
                Stats.Happiness += 5;
                break;
            case CareAction.Sleep:
                IsAsleep = true;
                break;
        }

        CareCount++;
        NoteActiveDay(now);
        RefreshConditions();
        return true;
    }

    // ---------- 활동 기록 (진화 조건) ----------

    /// <summary>
    /// 자리에 있던 시간을 더한다.
    /// 무슨 키를 눌렀는지는 인자로 받지도 않는다. 받을 수 없게 만든 것이 요점이다.
    /// </summary>
    public void AddFocusTime(TimeSpan focused, DateTimeOffset now)
    {
        if (focused <= TimeSpan.Zero) return;

        FocusSeconds += focused.TotalSeconds;
        NoteActiveDay(now);
        TryEvolve();
    }

    private void NoteActiveDay(DateTimeOffset now)
        => _activeDays.Add(now.ToString("yyyy-MM-dd"));

    private void TryEvolve()
    {
        if (Stage != LifeStage.Adult || EvolutionTier >= 2) return;

        var focusHours = FocusSeconds / 3600.0;
        var ready = EvolutionTier switch
        {
            0 => focusHours >= Balance.EvolutionFocusHours
                 && DistinctActiveDays >= Balance.EvolutionDistinctDays,
            1 => focusHours >= Balance.EvolutionFocusHours * 2
                 && CareCount >= Balance.EvolutionCareCount,
            _ => false,
        };
        if (!ready) return;

        EvolutionTier++;
        Evolved?.Invoke(EvolutionTier);
    }

    // ---------- 저장/복원 ----------

    public SaveData ToSave(DateTimeOffset now) => new()
    {
        SpeciesId = SpeciesId,
        Stage = Stage,
        EvolutionTier = EvolutionTier,
        BornAt = BornAt,
        LastSeenAt = now,
        Hunger = Stats.Hunger,
        Happiness = Stats.Happiness,
        Energy = Stats.Energy,
        Health = Stats.Health,
        Cleanliness = Stats.Cleanliness,
        HatchClicks = HatchClicks,
        IsSick = IsSick,
        IsSulking = IsSulking,
        FocusSeconds = FocusSeconds,
        CareCount = CareCount,
        ActiveDays = _activeDays.ToList(),
    };

    public static Pet FromSave(SaveData d)
    {
        var pet = new Pet
        {
            SpeciesId = d.SpeciesId,
            Stage = d.Stage,
            EvolutionTier = d.EvolutionTier,
            BornAt = d.BornAt,
            HatchClicks = d.HatchClicks,
            IsSick = d.IsSick,
            IsSulking = d.IsSulking,
            FocusSeconds = d.FocusSeconds,
            CareCount = d.CareCount,
            EggCreatedAt = d.BornAt ?? d.LastSeenAt,
        };

        pet.Stats.Hunger = d.Hunger;
        pet.Stats.Happiness = d.Happiness;
        pet.Stats.Energy = d.Energy;
        pet.Stats.Health = d.Health;
        pet.Stats.Cleanliness = d.Cleanliness;

        foreach (var day in d.ActiveDays) pet._activeDays.Add(day);
        return pet;
    }
}
