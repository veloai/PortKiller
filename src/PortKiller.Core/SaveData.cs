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
    public int PoopsOnGround { get; set; }
    public DateTimeOffset? NextPoopAt { get; set; }
    public bool IsSick { get; set; }
    public bool IsSulking { get; set; }

    public double FocusSeconds { get; set; }
    public int CareCount { get; set; }
    public List<string> ActiveDays { get; set; } = new();

    public NotebookData? Notebook { get; set; }

    /// <summary>
    /// 마지막으로 서 있던 가로 위치. 창 왼쪽에서부터의 픽셀이다.
    ///
    /// <para>왜 저장하나: 안 하면 켤 때마다 화면 <b>정중앙</b>으로 돌아온다. 거슬려서 옆으로
    /// 옮겨 놔도 다음 날 다시 한가운데에 서 있으면, 사용자 입장에서는 "옮기기가 안 되는" 것이다.</para>
    ///
    /// <para>세로는 저장하지 않는다 — 펫은 늘 바닥에 선다. null 이면 아직 한 번도 안 옮긴 것이다.</para>
    /// </summary>
    public double? LastX { get; set; }
}

public interface ISaveStore
{
    SaveData Load();
    void Save(SaveData data);
}
