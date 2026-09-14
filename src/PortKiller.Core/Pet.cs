namespace PortKiller.Core;

/// <summary>
/// 펫의 두뇌. 시간이 흐르면 무슨 일이 벌어지는지를 전부 여기서 결정한다.
/// 화면·윈도우·파일에 대해 아무것도 모른다. 그래서 단독으로 테스트된다.
/// </summary>
public sealed class Pet
{
    private readonly HashSet<string> _activeDays = new();
    private readonly Random _rng;

    /// <param name="createdAt">
    /// 알이 생긴 시각. 테스트가 시계에 흔들리지 않도록 바깥에서 넣을 수 있게 열어둔다.
    /// </param>
    /// <param name="rng">
    /// 응아 간격처럼 무작위가 섞이는 곳에 쓴다. 테스트에서는 씨앗을 고정해 넣는다.
    /// </param>
    public Pet(DateTimeOffset? createdAt = null, Random? rng = null)
    {
        EggCreatedAt = createdAt ?? DateTimeOffset.Now;
        _rng = rng ?? Random.Shared;
    }

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

    /// <summary>바닥에 있는 응아 개수. 많을수록 청결도가 빨리 떨어진다.</summary>
    public int PoopsOnGround { get; private set; }

    /// <summary>다음에 응아를 눌 시각. 먹이면 앞당겨진다.</summary>
    public DateTimeOffset? NextPoopAt { get; private set; }

    public event Action<LifeStage>? StageChanged;
    public event Action<int>? Evolved;
    public event Action<bool>? SickChanged;

    /// <summary>응아를 눌 때마다 몇 개 늘었는지 알린다. 화면은 이때 그림을 하나 놓는다.</summary>
    public event Action<int>? Pooped;

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
        {
            Stats.Energy += Balance.SleepEnergyRecoveryPerHour * hours;
            // 기운을 다 채우면 알아서 깬다. 안 그러면 재운 뒤 영영 자고 있게 된다.
            if (Stats.Energy >= 100) IsAsleep = false;
        }
        else
        {
            Stats.Energy -= Balance.EnergyDecayPerHour * hours;
        }

        ApplyPoop(now, offline, hours);
        ApplyHealth(hours);
        RefreshConditions();
        TryGrow(now);
    }

    // ---------- 응아 ----------

    /// <summary>
    /// 눌 때가 됐으면 눈다.
    /// 꺼둔 사이의 시간을 한 번에 따라잡을 때는 여러 개가 한꺼번에 나오는데,
    /// 화면이 뒤덮이지 않도록 개수를 막아둔다.
    /// </summary>
    /// <param name="hours">
    /// 이번에 흘러간 시간. 더러워지는 양은 반드시 여기에 비례해야 한다.
    /// 호출 횟수에 비례시키면 화면이 초당 60번 도는 순간 청결도가 몇 초 만에 0이 된다.
    /// </param>
    private void ApplyPoop(DateTimeOffset now, bool offline, double hours)
    {
        NextPoopAt ??= now.AddMinutes(
            Pick(Balance.PoopIntervalMinutesMin, Balance.PoopIntervalMinutesMax));

        var maxThisRound = offline ? Balance.MaxOfflinePoops : 1;
        var made = 0;

        while (now >= NextPoopAt && made < maxThisRound)
        {
            if (PoopsOnGround < Balance.MaxPoopsOnGround)
            {
                PoopsOnGround++;
                made++;
            }
            NextPoopAt = NextPoopAt.Value.AddMinutes(
                Pick(Balance.PoopIntervalMinutesMin, Balance.PoopIntervalMinutesMax));

            // 바닥이 꽉 찼으면 시각만 미루고 개수는 늘리지 않는다. 무한 반복 방지.
            if (PoopsOnGround >= Balance.MaxPoopsOnGround) break;
        }

        // 바닥에 쌓인 만큼 시간당 더러워진다.
        if (PoopsOnGround > 0)
            Stats.Cleanliness -= Balance.PoopDirtiness * PoopsOnGround * hours;

        if (made > 0) Pooped?.Invoke(made);
    }

    /// <summary>응아 하나를 치운다. 치울 게 없으면 false.</summary>
    public bool CleanOnePoop()
    {
        if (PoopsOnGround <= 0) return false;
        PoopsOnGround--;
        Stats.Cleanliness += Balance.PoopDirtiness;
        return true;
    }

    private double Pick(double min, double max) => min + _rng.NextDouble() * (max - min);

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

    /// <summary>돌보기를 적용한다. 못 하는 행동이면 이유를 돌려주고 아무것도 바꾸지 않는다.</summary>
    public CareResult ApplyCare(CareAction action, DateTimeOffset now)
    {
        if (Stage == LifeStage.Egg) return CareResult.StillAnEgg;

        switch (action)
        {
            case CareAction.Feed:
                Stats.Hunger += 30;
                ScheduleDigestion(now);
                break;
            case CareAction.Snack:
                Stats.Hunger += 10;
                Stats.Happiness += 5;
                ScheduleDigestion(now);
                break;
            case CareAction.Play:
                if (IsSick) return CareResult.TooSickToPlay;
                Stats.Happiness += 20;
                Stats.Energy -= 10;
                IsAsleep = false;
                break;
            case CareAction.Medicine:
                if (!IsSick) return CareResult.NotSick;
                Stats.Health += 40;
                break;
            case CareAction.Clean:
                // 치울 응아가 없으면 청결도만 올려주는 게 아니라 아예 거절한다.
                // 버튼을 계속 눌러 청결도를 100으로 채우는 길을 막는 것이기도 하다.
                if (!CleanOnePoop()) return CareResult.AlreadyClean;
                break;
            case CareAction.Pet:
                Stats.Happiness += 5;
                break;
            case CareAction.Sleep:
                if (IsAsleep) return CareResult.AlreadyAsleep;
                IsAsleep = true;
                break;
        }

        CareCount++;
        NoteActiveDay(now);
        RefreshConditions();
        return CareResult.Done;
    }

    /// <summary>
    /// 먹으면 소화 시간 뒤에 눈다. 원래 예정보다 늦어지지는 않게 이른 쪽을 택한다.
    /// (계속 먹이는데 응아가 안 나오면 먹인 보람이 없다)
    /// </summary>
    private void ScheduleDigestion(DateTimeOffset now)
    {
        var digested = now.AddMinutes(Pick(Balance.DigestMinutesMin, Balance.DigestMinutesMax));
        NextPoopAt = NextPoopAt is null || digested < NextPoopAt ? digested : NextPoopAt;
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
        PoopsOnGround = PoopsOnGround,
        NextPoopAt = NextPoopAt,
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
            PoopsOnGround = d.PoopsOnGround,
            NextPoopAt = d.NextPoopAt,
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
