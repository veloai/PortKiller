namespace PortKiller.Core;

/// <summary>
/// 돌보기를 시도한 결과.
///
/// 참/거짓만 돌려주면 화면이 "왜 안 됐는지"를 알 수 없어서
/// 말풍선에 쓸 말을 고를 수 없다. 그래서 이유까지 돌려준다.
/// </summary>
public enum CareResult
{
    Done,

    /// <summary>아직 알이라 아무것도 못 해준다.</summary>
    StillAnEgg,

    /// <summary>아프지 않은데 약을 줬다.</summary>
    NotSick,

    /// <summary>아파서 놀 기운이 없다.</summary>
    TooSickToPlay,

    /// <summary>치울 게 없다.</summary>
    AlreadyClean,

    /// <summary>이미 자고 있다.</summary>
    AlreadyAsleep,
}
