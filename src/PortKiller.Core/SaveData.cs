namespace PortKiller.Core;

/// <summary>디스크에 남는 전부. 이 파일을 지우면 흔적이 0이 된다.</summary>
public sealed class SaveData
{
    public int Version { get; set; } = 1;

    public string SpeciesId { get; set; } = "";
    public LifeStage Stage { get; set; } = LifeStage.Egg;
    public int EvolutionTier { get; set; }

    public DateTimeOffset? BornAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.Now;

    public double Hunger { get; set; } = 80;
    public double Happiness { get; set; } = 80;
    public double Energy { get; set; } = 80;
    public double Health { get; set; } = 100;
    public double Cleanliness { get; set; } = 100;

    public int HatchClicks { get; set; }
    public bool IsSick { get; set; }
    public bool IsSulking { get; set; }

    public double FocusSeconds { get; set; }
    public int CareCount { get; set; }
    public List<string> ActiveDays { get; set; } = new();
}

public interface ISaveStore
{
    SaveData Load();
    void Save(SaveData data);
}
