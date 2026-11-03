namespace PortKiller.Core;

/// <summary>개발 환경 항목 하나의 상태.</summary>
public enum DevState
{
    /// <summary>정상(돌고 있다).</summary>
    Ok,

    /// <summary>멈췄다·없다.</summary>
    Down,

    /// <summary>확인할 수 없다(예: Docker 엔진이 꺼져 있어 컨테이너를 못 봄, docker 명령이 없음).</summary>
    Unknown,
}

/// <summary>무엇을 확인할지. 종류는 docker-desktop / docker-engine / container / port 중 하나다.</summary>
public sealed record DevCheckSpec(string Kind, string Name, string Target);

/// <summary>확인 결과 한 줄.</summary>
public sealed record DevCheck(string Name, DevState State, string Detail);

/// <summary>상태가 바뀐 일(정상 → 멈춤, 멈춤 → 정상).</summary>
public sealed record DevChange(string Name, DevState From, DevState To);

/// <summary>
/// 개발 환경 상태를 사람 말로 바꾸고, 지난번과 무엇이 달라졌는지 가린다. 화면도 OS 도 모르는 규칙이라 시험할 수 있다.
/// </summary>
public static class DevStatusRules
{
    /// <summary>
    /// 기본 확인 목록. 이 PC 에서 매일 쓰는 것들이다. <c>devstatus.json</c> 으로 바꿀 수 있다.
    /// </summary>
    public static IReadOnlyList<DevCheckSpec> Defaults { get; } = new[]
    {
        new DevCheckSpec("docker-desktop", "Docker Desktop", "Docker Desktop"),
        new DevCheckSpec("docker-engine", "Docker 엔진", ""),
        new DevCheckSpec("container", "hermes", "hermes"),
        new DevCheckSpec("container", "alfa-backend", "alfa-backend"),
        new DevCheckSpec("container", "alfa-pg", "alfa-pg"),
        new DevCheckSpec("port", "Hermes 브리지", "47411"),
    };

    /// <summary>
    /// 지난번(<paramref name="before"/>)과 지금 사이에 정상↔멈춤으로 바뀐 것만 고른다.
    /// "확인 불가"로 가거나 오는 것은 알리지 않는다 - Docker 가 막 켜지는 중일 때마다 떠들면 잔소리가 된다.
    /// 처음 확인(<paramref name="before"/> 가 null)에는 아무것도 바뀐 것으로 치지 않는다.
    /// </summary>
    public static IReadOnlyList<DevChange> Diff(IReadOnlyList<DevCheck>? before, IReadOnlyList<DevCheck> after)
    {
        if (before is null) return Array.Empty<DevChange>();

        var old = before.ToDictionary(c => c.Name, c => c.State);
        var changes = new List<DevChange>();
        foreach (var now in after)
        {
            if (!old.TryGetValue(now.Name, out var was)) continue;
            if (was == now.State) continue;
            if (was == DevState.Unknown || now.State == DevState.Unknown) continue;
            changes.Add(new DevChange(now.Name, was, now.State));
        }

        return changes;
    }

    /// <summary>바뀐 일에 대한 펫의 한마디.</summary>
    public static string Line(DevChange change) => change.To == DevState.Down
        ? $"{change.Name} 이(가) 멈췄어요! 확인해 주세요."
        : $"{change.Name} 다시 살아났어요.";

    /// <summary>전체 요약. 문제가 있는 것만 이름을 댄다.</summary>
    public static string Summary(IReadOnlyList<DevCheck> checks)
    {
        if (checks.Count == 0) return "확인할 항목이 없어요.";

        var down = checks.Where(c => c.State == DevState.Down).Select(c => c.Name).ToList();
        if (down.Count > 0) return $"{checks.Count}개 중 {down.Count}개 문제: {string.Join(", ", down)}";

        var unknown = checks.Count(c => c.State == DevState.Unknown);
        return unknown > 0 ? $"정상 {checks.Count - unknown}개, 확인 불가 {unknown}개" : "개발 환경 모두 정상이에요.";
    }

    /// <summary>한 항목을 짚었을 때의 설명(브리핑 카드에서 마우스로 가리킬 때).</summary>
    public static string Describe(DevCheck check) => check.State switch
    {
        DevState.Ok => $"{check.Name}: 잘 돌고 있어요. {check.Detail}".TrimEnd(),
        DevState.Down => $"{check.Name}: 멈춰 있어요. {check.Detail}".TrimEnd(),
        _ => $"{check.Name}: 지금은 확인할 수 없어요. {check.Detail}".TrimEnd(),
    };

    /// <summary>
    /// <c>docker ps -a --format "{{.Names}}|{{.State}}|{{.Status}}"</c> 의 출력을 이름 → (상태, 설명)으로 읽는다.
    /// </summary>
    public static IReadOnlyDictionary<string, (string State, string Status)> ParseDockerPs(string output)
    {
        var map = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split('|');
            if (parts.Length < 2) continue;
            map[parts[0].Trim()] = (parts[1].Trim(), parts.Length > 2 ? parts[2].Trim() : string.Empty);
        }

        return map;
    }
}
