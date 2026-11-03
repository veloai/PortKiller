namespace PortKiller.Core;

/// <summary>하루치 기억. 날짜는 "yyyy-MM-dd"(내 PC 시간대).</summary>
public sealed class DayRecord
{
    public string Date { get; set; } = "";

    /// <summary>그날 처음 활동한 시각(자정부터 몇 분). 활동이 없었으면 null.</summary>
    public int? FirstActiveMinute { get; set; }

    public double FocusSeconds { get; set; }
    public int Pomodoros { get; set; }
    public int Cares { get; set; }
    public int TodosDone { get; set; }
}

/// <summary>기억 파일에 남는 전부(<c>memory.json</c>). 세이브와 따로 두어, 새 펫을 맞이해도 "나"에 대한 기억은 남는다.</summary>
public sealed class MemoryData
{
    public int Version { get; set; } = 1;
    public List<DayRecord> Days { get; set; } = new();

    /// <summary>지난번에 본 누적값. 다음에 볼 때 늘어난 만큼만 오늘에 더한다.</summary>
    public double LastFocusTotal { get; set; } = -1;
    public int LastCareTotal { get; set; } = -1;
    public int LastPomodoroTotal { get; set; } = -1;

    /// <summary>그날 첫 인사·기록 갱신 알림을 이미 했는지(하루 한 번만 말하게).</summary>
    public string LastGreetedDate { get; set; } = "";
    public string LastRecordDate { get; set; } = "";
}

/// <summary>
/// 펫의 장기 기억. 앱이 이미 아는 값(활동 여부, 누적 집중 시간·돌보기·뽀모도로, 오늘 끝낸 할 일 수)만 날짜별로 모은다.
/// 새로 감시하는 것은 없고, 입력 내용·창·화면은 전혀 모른다. 최근 <see cref="KeepDays"/>일만 남긴다.
/// </summary>
public sealed class PetMemory
{
    public const int KeepDays = 90;

    public MemoryData Data { get; }

    public PetMemory(MemoryData? data = null) => Data = data ?? new MemoryData();

    public static string DateOf(DateTimeOffset t) => t.ToString("yyyy-MM-dd");

    public DayRecord Today(DateTimeOffset now) => Day(DateOf(now), create: true)!;

    public DayRecord? Day(string date, bool create = false)
    {
        var d = Data.Days.FirstOrDefault(x => x.Date == date);
        if (d is null && create)
        {
            d = new DayRecord { Date = date };
            Data.Days.Add(d);
            Data.Days.Sort((a, b) => string.CompareOrdinal(a.Date, b.Date));
        }

        return d;
    }

    /// <summary>
    /// 지금 상태를 한 번 본다(15초쯤마다). 누적값이 줄었으면(새 펫을 맞이함) 늘어난 것이 없는 것으로 본다.
    /// 그날 처음으로 활동이 보이면 true 를 돌려준다(첫 출근 인사를 할 때).
    /// </summary>
    public bool Observe(DateTimeOffset now, bool userActive, double focusTotal, int careTotal, int pomodoroTotal, int todosDoneToday)
    {
        var today = Today(now);
        var arrived = false;
        if (userActive && today.FirstActiveMinute is null)
        {
            today.FirstActiveMinute = now.Hour * 60 + now.Minute;
            arrived = true;
        }

        today.FocusSeconds += Delta(focusTotal, Data.LastFocusTotal);
        today.Cares += (int)Delta(careTotal, Data.LastCareTotal);
        today.Pomodoros += (int)Delta(pomodoroTotal, Data.LastPomodoroTotal);
        today.TodosDone = Math.Max(today.TodosDone, todosDoneToday);

        Data.LastFocusTotal = focusTotal;
        Data.LastCareTotal = careTotal;
        Data.LastPomodoroTotal = pomodoroTotal;

        Prune(now);
        return arrived;
    }

    /// <summary>처음 보는 값(-1)이거나 줄었으면 0, 아니면 늘어난 만큼.</summary>
    private static double Delta(double total, double last) => last < 0 || total < last ? 0 : total - last;

    private void Prune(DateTimeOffset now)
    {
        var cutoff = DateOf(now.AddDays(-KeepDays));
        Data.Days.RemoveAll(d => string.CompareOrdinal(d.Date, cutoff) < 0);
    }

    /// <summary>오늘 전까지, 활동이 있었던 날들(최근순).</summary>
    public IEnumerable<DayRecord> PastActiveDays(DateTimeOffset now)
    {
        var today = DateOf(now);
        return Data.Days.Where(d => string.CompareOrdinal(d.Date, today) < 0 && d.FirstActiveMinute is not null)
                        .OrderByDescending(d => d.Date);
    }
}

/// <summary>기억을 바탕으로 펫이 하는 말. 순수 규칙이라 시험할 수 있다.</summary>
public static class MemoryInsights
{
    /// <summary>평소보다 이만큼(분) 이상 늦거나 이르면 말한다.</summary>
    public const int ArrivalDiffMinutes = 30;

    /// <summary>
    /// 그날 첫 활동 때 하는 한마디. 할 말이 없으면 null(그냥 평소 인사).
    /// 우선순위: 오랜만 → 평소보다 늦음/이름 → 연속 출근 → 어제 집중.
    /// </summary>
    public static string? ArrivalLine(PetMemory memory, DateTimeOffset now)
    {
        var today = memory.Day(PetMemory.DateOf(now));
        var past = memory.PastActiveDays(now).ToList();
        if (past.Count == 0) return null;

        // 1) 오랜만에 왔다: 평일을 이틀 이상 비웠을 때만(금요일 → 월요일은 오랜만이 아니다)
        var last = DateTime.Parse(past[0].Date);
        var gap = (now.Date - last).Days;
        var missedWorkdays = Enumerable.Range(1, Math.Max(0, gap - 1))
            .Count(i => last.AddDays(i).DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday));
        if (missedWorkdays >= 2) return $"{gap}일 만이에요! 보고 싶었어요.";

        // 2) 평소 출근 시각과 비교(최근 14일, 3번 이상 있어야)
        if (today?.FirstActiveMinute is { } arrivedAt)
        {
            var recent = past.Take(14).Select(d => d.FirstActiveMinute!.Value).ToList();
            if (recent.Count >= 3)
            {
                var usual = recent.OrderBy(x => x).ElementAt(recent.Count / 2); // 중앙값: 하루 튀는 날에 흔들리지 않게
                var diff = arrivedAt - usual;
                if (diff >= ArrivalDiffMinutes) return $"오늘은 평소보다 {Duration(diff)} 늦었네요. 무슨 일 있었어요?";
                if (diff <= -ArrivalDiffMinutes) return $"오늘은 평소보다 {Duration(-diff)} 일찍 왔네요! 부지런해요.";
            }
        }

        // 3) 연속 출근(오늘 포함)
        var streak = Streak(memory, now);
        if (streak >= 3) return $"{streak}일 연속 출근이에요! 대단해요.";

        // 4) 어제 집중
        var yesterday = memory.Day(PetMemory.DateOf(now.AddDays(-1)));
        if (yesterday is { FocusSeconds: >= 3600 })
            return $"어제는 {yesterday.FocusSeconds / 3600:F1}시간 집중했어요. 오늘도 같이 해요!";

        return null;
    }

    /// <summary>오늘을 포함해 하루도 빠짐없이 활동한 날 수. 주말(토·일)은 쉬어도 끊지 않는다.</summary>
    public static int Streak(PetMemory memory, DateTimeOffset now)
    {
        var count = 0;
        for (var i = 0; i < PetMemory.KeepDays; i++)
        {
            var d = now.Date.AddDays(-i);
            if (memory.Day(d.ToString("yyyy-MM-dd"))?.FirstActiveMinute is not null) count++;
            else if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) break;
        }

        return count;
    }

    /// <summary>
    /// 오늘 집중 시간이 지난 기록(활동한 날 7일 이상 쌓였을 때)을 넘었으면 한마디. 하루 한 번만(호출하는 쪽이 날짜를 기록).
    /// </summary>
    public static string? RecordLine(PetMemory memory, DateTimeOffset now)
    {
        var today = memory.Day(PetMemory.DateOf(now));
        if (today is null || today.FocusSeconds < 1800) return null;

        var past = memory.PastActiveDays(now).ToList();
        if (past.Count < 7) return null;

        var best = past.Max(d => d.FocusSeconds);
        return today.FocusSeconds > best ? $"오늘 집중 신기록! {today.FocusSeconds / 3600:F1}시간이에요 🎉" : null;
    }

    /// <summary>이번 주(월요일부터) 요약 한 줄.</summary>
    public static string WeekSummary(PetMemory memory, DateTimeOffset now)
    {
        var monday = now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7));
        var week = memory.Data.Days.Where(d => DateTime.TryParse(d.Date, out var dt) && dt >= monday && dt <= now.Date).ToList();
        var focus = week.Sum(d => d.FocusSeconds) / 3600;
        var days = week.Count(d => d.FirstActiveMinute is not null);
        var pomo = week.Sum(d => d.Pomodoros);
        var best = week.OrderByDescending(d => d.FocusSeconds).FirstOrDefault(d => d.FocusSeconds > 0);
        var bestText = best is null ? "" : $" · 최고 {Weekday(best.Date)} {best.FocusSeconds / 3600:F1}h";
        return $"이번 주 집중 {focus:F1}h · 출근 {days}일 · 뽀모도로 {pomo}회{bestText}";
    }

    /// <summary>분을 읽기 쉬운 말로: 50 → "50분", 60 → "1시간", 375 → "6시간 15분".</summary>
    public static string Duration(int minutes)
    {
        if (minutes < 60) return $"{minutes}분";
        var h = minutes / 60;
        var m = minutes % 60;
        return m == 0 ? $"{h}시간" : $"{h}시간 {m}분";
    }

    private static string Weekday(string date) => DateTime.Parse(date).DayOfWeek switch
    {
        DayOfWeek.Monday => "월", DayOfWeek.Tuesday => "화", DayOfWeek.Wednesday => "수", DayOfWeek.Thursday => "목",
        DayOfWeek.Friday => "금", DayOfWeek.Saturday => "토", _ => "일",
    };
}
