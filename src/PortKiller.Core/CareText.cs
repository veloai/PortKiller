namespace PortKiller.Core;

/// <summary>
/// 펫이 하는 말.
///
/// 대사를 화면 코드에 흩어 놓으면 말투를 고칠 때마다 여러 파일을 뒤져야 하고,
/// 빠진 경우(대사가 없는 상황)를 찾을 방법이 없어진다. 그래서 한곳에 모으고
/// "모든 경우에 대사가 있는지"를 시험으로 막는다.
/// </summary>
public static class CareText
{
    private static readonly Dictionary<CareAction, string[]> Success = new()
    {
        [CareAction.Feed] = new[] { "냠냠! 잘 먹었어", "우와 배부르다!", "이거 맛있네" },
        [CareAction.Snack] = new[] { "간식이다!", "조금만 더 줘…", "오독오독" },
        [CareAction.Play] = new[] { "재밌다!", "한 판 더!", "히히" },
        [CareAction.Medicine] = new[] { "으… 쓰다", "좀 나아진 것 같아", "고마워…" },
        [CareAction.Clean] = new[] { "개운해!", "깨끗해졌다", "치워줘서 고마워" },
        [CareAction.Pet] = new[] { "헤헤", "좋아…", "더 해줘" },
        [CareAction.Sleep] = new[] { "잘자…", "쿨… 쿨…", "조금만 잘게" },
    };

    private static readonly Dictionary<CareResult, string> Refusal = new()
    {
        [CareResult.StillAnEgg] = "아직 알이라서 못 해…",
        [CareResult.NotSick] = "나 안 아픈데? 마음만 받을게",
        [CareResult.TooSickToPlay] = "아파서 기운이 없어. 약(💊) 먼저 줄래?",
        [CareResult.AlreadyClean] = "이미 깨끗한걸?",
        [CareResult.AlreadyAsleep] = "쿨… 쿨…",
    };

    private static readonly string[] GreetingLines =
    {
        "왔구나! 오늘도 같이 일하자",
        "안녕! 나 여기 있어",
        "출근했네. 잘 부탁해!",
    };

    public const string EggGreeting = "알이 도착했어. 클릭해서 깨워줘!";
    public const string Hatched = "안녕! 나 이제 나왔어";

    /// <summary>처음 만났을 때 한마디. 알형은 알이 왔다고, 새끼형은 바구니에 잠든 새끼가 있다고 한다.</summary>
    public static string NewbornGreeting(PetKind kind) => kind.Birth == BirthKind.Egg
        ? EggGreeting
        : $"바구니에 {kind.Name} 새끼가 잠들어 있어. 쓰다듬어서 깨워줘!";

    /// <summary>깨어났을 때 한마디.</summary>
    public static string Born(PetKind kind) => kind.Birth == BirthKind.Egg
        ? Hatched
        : "으음… 깼다! 안녕!";

    /// <summary>아직 못 깬 새끼에게 돌보기를 했을 때. 알형·새끼형이 다른 말을 한다.</summary>
    public static string StillNewborn(PetKind kind) => kind.Birth == BirthKind.Egg
        ? "아직 알이라서 못 해…"
        : "쿨… 아직 자고 있어서 못 해…";

    /// <summary>트레이 툴팁처럼 짧은 안내.</summary>
    public static string NewbornHint(PetKind kind) => kind.Birth == BirthKind.Egg
        ? "아직 알이야. 클릭해서 깨워줘!"
        : "아직 자고 있어. 쓰다듬어서 깨워줘!";

    /// <summary>돌보기 결과에 맞는 한 마디. 어떤 조합이 와도 빈 문자열을 돌려주지 않는다.</summary>
    public static string For(CareAction action, CareResult result, Random? rng = null, PetKind? kind = null)
    {
        if (result == CareResult.StillAnEgg && kind is not null) return StillNewborn(kind);

        if (result != CareResult.Done)
            return Refusal.TryGetValue(result, out var refusal) ? refusal : "지금은 안 되겠어…";

        var lines = Success[action];
        return lines[(rng ?? Random.Shared).Next(lines.Length)];
    }

    public static string Greeting(LifeStage stage, Random? rng = null, PetKind? kind = null)
        => stage == LifeStage.Egg
            ? (kind is null ? EggGreeting : NewbornGreeting(kind))
            : GreetingLines[(rng ?? Random.Shared).Next(GreetingLines.Length)];

    // ---------- AI ----------

    /// <summary>
    /// 답이 도착했을 때 펫이 하는 한마디. <b>답 내용은 넣지 않는다</b> -
    /// 말풍선은 몇 글자짜리라 AI 답을 담으면 화면을 덮고, 지나가던 사람도 읽게 된다.
    /// 여기서는 "왔다"만 알리고 내용은 물어보기 창에 둔다.
    /// </summary>
    private static readonly string[] AiAnsweredLines =
    {
        "답 왔어! 창 봐봐",
        "알아왔어~",
        "다 찾아봤어!",
        "이거 맞나 봐봐",
    };

    public static string AiAnswered(Random? rng = null)
        => AiAnsweredLines[(rng ?? Random.Shared).Next(AiAnsweredLines.Length)];

    // ---------- 수첩 ----------

    public static string Reminder(string what) => $"⏰ {what} 할 시간이야!";

    public static string TodoDone(int done, int total)
        => done >= total ? "🎉 오늘 할 일 전부 끝! 대단해!" : $"좋아! {done}/{total} 했다";

    public static string Pomodoro(PomodoroPhase phase) => phase switch
    {
        PomodoroPhase.Focus => "집중 시작! 나도 조용히 있을게",
        PomodoroPhase.Break => "25분 끝! 5분만 쉬자",
        _ => "수고했어!",
    };

    public static string Evolved(int tier)
        => tier >= 2 ? "✨✨ 최종 진화! ✨✨" : "✨ 진화했다! ✨";

    /// <summary>단계가 오를 때 한마디. 깨어날 때(아기가 될 때)는 종의 태어난 방식에 맞게 말한다.</summary>
    public static string StageUp(LifeStage stage, PetKind kind)
        => stage == LifeStage.Baby ? Born(kind) : StageUp(stage);

    public static string StageUp(LifeStage stage) => stage switch
    {
        LifeStage.Baby => "삐약!",
        LifeStage.Child => "조금 컸지?",
        LifeStage.Adult => "이제 다 컸어!",
        _ => "…",
    };

    /// <summary>시험이 모든 경우를 훑을 수 있도록 목록을 열어 둔다.</summary>
    public static IEnumerable<CareAction> AllActions => Success.Keys;
    public static IEnumerable<CareResult> AllRefusals => Refusal.Keys;
}
