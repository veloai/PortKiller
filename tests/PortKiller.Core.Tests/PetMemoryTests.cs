using PortKiller.Core;
using Xunit;

namespace PortKiller.Core.Tests;

public class PetMemoryTests
{
    private static readonly TimeSpan Kst = TimeSpan.FromHours(9);

    // 2026-11-02 은 월요일이다.
    private static DateTimeOffset At(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, Kst);

    /// <summary>그날 지정한 시각에 처음 활동한 기록을 만든다.</summary>
    private static void Arrive(PetMemory m, DateTimeOffset t, double focusHours = 0)
    {
        var d = m.Today(t);
        d.FirstActiveMinute = t.Hour * 60 + t.Minute;
        d.FocusSeconds = focusHours * 3600;
    }

    [Fact]
    public void First_activity_of_the_day_is_reported_once_and_its_time_kept()
    {
        var m = new PetMemory();

        Assert.False(m.Observe(At(11, 3, 9, 0), userActive: false, 0, 0, 0, 0));
        Assert.True(m.Observe(At(11, 3, 9, 12), userActive: true, 0, 0, 0, 0));
        Assert.False(m.Observe(At(11, 3, 9, 30), userActive: true, 0, 0, 0, 0));
        Assert.Equal(9 * 60 + 12, m.Day("2026-11-03")!.FirstActiveMinute);
    }

    [Fact]
    public void Totals_add_only_what_grew_and_a_reset_adds_nothing()
    {
        var m = new PetMemory();
        m.Observe(At(11, 3, 9), true, focusTotal: 1000, careTotal: 10, pomodoroTotal: 2, todosDoneToday: 0); // 처음 본 값은 기준점
        m.Observe(At(11, 3, 10), true, focusTotal: 1600, careTotal: 12, pomodoroTotal: 3, todosDoneToday: 1);
        m.Observe(At(11, 3, 11), true, focusTotal: 100, careTotal: 0, pomodoroTotal: 0, todosDoneToday: 2);   // 새 펫: 누적값이 줄었다
        m.Observe(At(11, 3, 12), true, focusTotal: 400, careTotal: 1, pomodoroTotal: 1, todosDoneToday: 1);

        var d = m.Day("2026-11-03")!;
        Assert.Equal(600 + 300, d.FocusSeconds);
        Assert.Equal(2 + 1, d.Cares);
        Assert.Equal(1 + 1, d.Pomodoros);
        Assert.Equal(2, d.TodosDone);   // 그날 본 가장 큰 값
    }

    [Fact]
    public void Old_days_are_forgotten_after_the_keep_window()
    {
        var m = new PetMemory();
        Arrive(m, At(7, 1, 9));
        m.Observe(At(11, 3, 9), true, 0, 0, 0, 0);

        Assert.Null(m.Day("2026-07-01"));
        Assert.NotNull(m.Day("2026-11-03"));
    }

    [Fact]
    public void Coming_back_after_days_away_is_noticed_first()
    {
        var m = new PetMemory();
        Arrive(m, At(10, 27, 9));
        Arrive(m, At(11, 3, 9));

        Assert.Contains("7일 만", MemoryInsights.ArrivalLine(m, At(11, 3, 9)));
    }

    [Fact]
    public void Arriving_much_later_or_earlier_than_usual_is_noticed()
    {
        var m = new PetMemory();
        foreach (var day in new[] { 27, 28, 29, 30 }) Arrive(m, At(10, day, 9, 0));   // 평소 9:00

        Arrive(m, At(11, 2, 9, 50));
        Assert.Contains("50분 늦었", MemoryInsights.ArrivalLine(m, At(11, 2, 9, 50)));

        var early = new PetMemory();
        foreach (var day in new[] { 27, 28, 29, 30 }) Arrive(early, At(10, day, 9, 0));
        Arrive(early, At(11, 2, 8, 0));
        Assert.Contains("1시간 일찍", MemoryInsights.ArrivalLine(early, At(11, 2, 8, 0)));
    }

    [Theory]
    [InlineData(50, "50분")]
    [InlineData(60, "1시간")]
    [InlineData(375, "6시간 15분")]
    public void Durations_read_naturally(int minutes, string expected)
    {
        Assert.Equal(expected, MemoryInsights.Duration(minutes));
    }

    [Fact]
    public void Streak_counts_consecutive_workdays_and_skips_weekends()
    {
        var m = new PetMemory();
        // 10/29(목) 10/30(금) 쉬는 주말 11/2(월) 11/3(화)
        foreach (var day in new[] { 29, 30 }) Arrive(m, At(10, day, 9));
        Arrive(m, At(11, 2, 9));
        Arrive(m, At(11, 3, 9));

        Assert.Equal(4, MemoryInsights.Streak(m, At(11, 3, 9)));
        Assert.Contains("4일 연속", MemoryInsights.ArrivalLine(m, At(11, 3, 9)));
    }

    [Fact]
    public void Yesterdays_focus_is_mentioned_when_nothing_else_stands_out()
    {
        var m = new PetMemory();
        Arrive(m, At(11, 2, 9), focusHours: 3.2);
        Arrive(m, At(11, 3, 9));

        Assert.Contains("어제는 3.2시간", MemoryInsights.ArrivalLine(m, At(11, 3, 9)));
    }

    [Fact]
    public void No_history_means_no_memory_line()
    {
        var m = new PetMemory();
        Arrive(m, At(11, 3, 9));

        Assert.Null(MemoryInsights.ArrivalLine(m, At(11, 3, 9)));
    }

    [Fact]
    public void A_new_focus_record_is_announced_only_with_enough_history()
    {
        var m = new PetMemory();
        for (var day = 20; day <= 30; day++) Arrive(m, At(10, day, 9), focusHours: 2);
        Arrive(m, At(11, 3, 9), focusHours: 2.5);

        Assert.Contains("신기록", MemoryInsights.RecordLine(m, At(11, 3, 17)));

        var thin = new PetMemory();
        Arrive(thin, At(11, 2, 9), focusHours: 1);
        Arrive(thin, At(11, 3, 9), focusHours: 5);
        Assert.Null(MemoryInsights.RecordLine(thin, At(11, 3, 17)));
    }

    [Fact]
    public void Week_summary_counts_from_monday()
    {
        var m = new PetMemory();
        Arrive(m, At(10, 30, 9), focusHours: 9);   // 지난주 금요일: 빠진다
        Arrive(m, At(11, 2, 9), focusHours: 2);
        Arrive(m, At(11, 3, 9), focusHours: 3);
        m.Day("2026-11-03")!.Pomodoros = 4;

        var s = MemoryInsights.WeekSummary(m, At(11, 3, 18));

        Assert.Contains("집중 5.0h", s);
        Assert.Contains("출근 2일", s);
        Assert.Contains("뽀모도로 4회", s);
        Assert.Contains("최고 화 3.0h", s);
    }
}
