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

    /// <summary>돌보기 결과에 맞는 한 마디. 어떤 조합이 와도 빈 문자열을 돌려주지 않는다.</summary>
    public static string For(CareAction action, CareResult result, Random? rng = null)
    {
        if (result != CareResult.Done)
            return Refusal.TryGetValue(result, out var refusal) ? refusal : "지금은 안 되겠어…";

        var lines = Success[action];
        return lines[(rng ?? Random.Shared).Next(lines.Length)];
    }

    public static string Greeting(LifeStage stage, Random? rng = null)
        => stage == LifeStage.Egg
            ? EggGreeting
            : GreetingLines[(rng ?? Random.Shared).Next(GreetingLines.Length)];

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
