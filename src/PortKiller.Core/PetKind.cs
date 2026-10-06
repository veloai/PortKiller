namespace PortKiller.Core;

/// <summary>태어나는 방식. 알에서 깨는 동물과, 새끼로 태어나 잠에서 깨는 동물로 나뉜다.</summary>
public enum BirthKind
{
    /// <summary>알형: 알이 흔들리다 금이 가고 부화한다. (병아리 등)</summary>
    Egg,

    /// <summary>새끼형: 바구니 속에서 자는 새끼를 쓰다듬어 깨운다. (포유류)</summary>
    Cub,
}

/// <summary>몸의 움직임 틀. 같은 틀을 쓰는 종은 모델 설계도와 색만 다르다.</summary>
public enum RigKind
{
    /// <summary>네발 포유류. 걷기·일어서기·꼬리 흔들기 틀을 공유한다.</summary>
    Quadruped,

    /// <summary>두 발 새. 뒤뚱거리며 걷고 날개로 짚는다.</summary>
    Biped,
}

/// <summary>
/// 키울 수 있는 동물 한 종. 이름·태어나는 방식·움직임 틀을 한곳에 모았다.
/// 모델(복셀)은 화면 쪽이 이 id 로 찾는다 — Core 는 모양을 모른다.
/// </summary>
public sealed record PetKind(string Id, string Name, BirthKind Birth, RigKind Rig, string Blurb)
{
    /// <summary>갓 태어났을 때의 모습 이름. 알형은 "알", 새끼형은 "새끼".</summary>
    public string NewbornNoun => Birth == BirthKind.Egg ? "알" : "새끼";
}

/// <summary>종 목록. 새 동물은 여기에 한 줄을 더하고 모델을 붙이면 된다.</summary>
public static class PetKinds
{
    public const string RedPanda = "red_panda";
    public const string Cat = "cat";
    public const string Fox = "fox";
    public const string Chick = "chick";

    public static readonly IReadOnlyList<PetKind> All = new[]
    {
        new PetKind(RedPanda, "레서판다", BirthKind.Cub, RigKind.Quadruped, "느긋하고 호기심이 많아요"),
        new PetKind(Cat, "고양이", BirthKind.Cub, RigKind.Quadruped, "도도한데 은근히 정이 많아요"),
        new PetKind(Fox, "여우", BirthKind.Cub, RigKind.Quadruped, "재빠르고 눈치가 빨라요"),
        new PetKind(Chick, "병아리", BirthKind.Egg, RigKind.Biped, "알에서 깨어나 뒤뚱뒤뚱 따라와요"),
    };

    /// <summary>아직 종을 고르지 않은 상태(새 저장). 이때는 선택 창을 띄운다.</summary>
    public static bool IsChosen(string? id) => All.Any(k => k.Id == id);

    /// <summary>id 로 종을 찾는다. 모르는 id(옛 저장·고르기 전)는 레서판다로 본다 - 앱이 죽지 않게.</summary>
    public static PetKind Get(string? id) => All.FirstOrDefault(k => k.Id == id) ?? All[0];
}
