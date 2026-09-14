using PortKiller.Core;
using Xunit;

namespace PortKiller.Core.Tests;

public class NotebookTests
{
    private static readonly DateTimeOffset Monday9 = new(2026, 9, 14, 9, 0, 0, TimeSpan.FromHours(9));
    private static readonly DateTimeOffset Saturday9 = new(2026, 9, 19, 9, 0, 0, TimeSpan.FromHours(9));

    private static Notebook Started(DateTimeOffset now)
    {
        var book = new Notebook();
        book.Tick(now);
        return book;
    }

    // ---------- 할 일 ----------

    [Fact]
    public void HasExactlyThreeSlots()
        => Assert.Equal(3, new Notebook().Todos.Count);

    [Fact]
    public void EmptySlotCannotBeChecked()
    {
        var book = Started(Monday9);
        book.SetTodoDone(0, true, Monday9);

        Assert.False(book.Todos[0].Done);
    }

    /// <summary>세 칸을 다 쓰고 다 끝내야 완주다. 한 칸만 쓰고 끝내는 건 아니다.</summary>
    [Fact]
    public void AllDoneNeedsAllThreeFilledAndChecked()
    {
        var book = Started(Monday9);
        var celebrations = 0;
        book.AllTodosDone += () => celebrations++;

        book.SetTodo(0, "코드 리뷰");
        book.SetTodoDone(0, true, Monday9);
        Assert.False(book.AllDone());
        Assert.Equal(0, celebrations);

        book.SetTodo(1, "배포");
        book.SetTodo(2, "회고");
        book.SetTodoDone(1, true, Monday9);
        book.SetTodoDone(2, true, Monday9);

        Assert.True(book.AllDone());
        Assert.Equal(1, celebrations);
    }

    /// <summary>축하는 한 번만. 체크를 껐다 켤 때마다 파티가 열리면 안 된다.</summary>
    [Fact]
    public void CelebratesOnlyOnCompletion()
    {
        var book = Started(Monday9);
        var celebrations = 0;
        book.AllTodosDone += () => celebrations++;

        for (var i = 0; i < 3; i++) book.SetTodo(i, $"할 일 {i}");
        for (var i = 0; i < 3; i++) book.SetTodoDone(i, true, Monday9);
        Assert.Equal(1, celebrations);

        book.SetTodoDone(2, false, Monday9);
        book.SetTodoDone(2, true, Monday9);
        Assert.Equal(2, celebrations);
    }

    /// <summary>"오늘 할 일"이므로 날이 바뀌면 비워진다.</summary>
    [Fact]
    public void TodosResetOnANewDay()
    {
        var book = Started(Monday9);
        book.SetTodo(0, "어제 것");
        book.SetTodoDone(0, true, Monday9);

        book.Tick(Monday9.AddDays(1));

        Assert.True(book.Todos[0].IsEmpty);
        Assert.False(book.Todos[0].Done);
    }

    [Fact]
    public void ClearingTextAlsoClearsTheCheck()
    {
        var book = Started(Monday9);
        book.SetTodo(0, "할 일");
        book.SetTodoDone(0, true, Monday9);

        book.SetTodo(0, "");

        Assert.False(book.Todos[0].Done);
    }

    // ---------- 리마인더 ----------

    [Fact]
    public void ReminderFiresOnceWhenItsTimeArrives()
    {
        var book = Started(Monday9);
        var fired = new List<string>();
        book.ReminderDue += r => fired.Add(r.Text);
        book.Reminders.Add(new Reminder { Text = "스탠드업", Hour = 10, Minute = 0 });

        book.Tick(Monday9.AddMinutes(30));   // 09:30 — 아직
        Assert.Empty(fired);

        book.Tick(Monday9.AddHours(1));      // 10:00 — 울린다
        Assert.Equal(new[] { "스탠드업" }, fired);

        book.Tick(Monday9.AddHours(2));      // 같은 날 또 울리면 안 된다
        Assert.Single(fired);
    }

    [Fact]
    public void OnceReminderTurnsItselfOff()
    {
        var book = Started(Monday9);
        book.Reminders.Add(new Reminder
        {
            Text = "한 번만", Hour = 10, Minute = 0, Repeat = ReminderRepeat.Once,
        });

        book.Tick(Monday9.AddHours(1));

        Assert.False(book.Reminders[0].Enabled);
    }

    [Fact]
    public void DailyReminderFiresAgainTomorrow()
    {
        var book = Started(Monday9);
        var count = 0;
        book.ReminderDue += _ => count++;
        book.Reminders.Add(new Reminder
        {
            Text = "약 먹기", Hour = 10, Minute = 0, Repeat = ReminderRepeat.Daily,
        });

        book.Tick(Monday9.AddHours(1));
        book.Tick(Monday9.AddDays(1).AddHours(1));

        Assert.Equal(2, count);
    }

    [Fact]
    public void WeekdayReminderStaysQuietOnTheWeekend()
    {
        var book = Started(Saturday9);
        var count = 0;
        book.ReminderDue += _ => count++;
        book.Reminders.Add(new Reminder
        {
            Text = "출근 준비", Hour = 10, Minute = 0, Repeat = ReminderRepeat.Weekdays,
        });

        book.Tick(Saturday9.AddHours(1));   // 토요일
        Assert.Equal(0, count);

        book.Tick(Saturday9.AddDays(2).AddHours(1));  // 월요일
        Assert.Equal(1, count);
    }

    [Fact]
    public void DisabledReminderNeverFires()
    {
        var book = Started(Monday9);
        var count = 0;
        book.ReminderDue += _ => count++;
        book.Reminders.Add(new Reminder { Text = "꺼둠", Hour = 10, Minute = 0, Enabled = false });

        book.Tick(Monday9.AddHours(5));

        Assert.Equal(0, count);
    }

    // ---------- 뽀모도로 ----------

    [Fact]
    public void FocusRunsThenBreakThenStops()
    {
        var book = Started(Monday9);
        var phases = new List<PomodoroPhase>();
        book.PhaseChanged += p => phases.Add(p);

        book.StartPomodoro(Monday9);
        Assert.Equal(PomodoroPhase.Focus, book.Phase);

        book.Tick(Monday9.AddMinutes(Balance.PomodoroFocusMinutes));
        Assert.Equal(PomodoroPhase.Break, book.Phase);
        Assert.Equal(1, book.PomodoroCompleted);

        book.Tick(Monday9.AddMinutes(Balance.PomodoroFocusMinutes + Balance.PomodoroBreakMinutes));
        Assert.Equal(PomodoroPhase.Idle, book.Phase);

        Assert.Equal(
            new[] { PomodoroPhase.Focus, PomodoroPhase.Break, PomodoroPhase.Idle },
            phases);
    }

    [Fact]
    public void RemainingCountsDownAndNeverGoesNegative()
    {
        var book = Started(Monday9);
        book.StartPomodoro(Monday9);

        Assert.Equal(
            TimeSpan.FromMinutes(Balance.PomodoroFocusMinutes),
            book.Remaining(Monday9));
        Assert.Equal(TimeSpan.FromMinutes(20), book.Remaining(Monday9.AddMinutes(5)));
        Assert.Equal(TimeSpan.Zero, book.Remaining(Monday9.AddHours(3)));
    }

    [Fact]
    public void StoppingEarlyDoesNotCount()
    {
        var book = Started(Monday9);
        book.StartPomodoro(Monday9);
        book.StopPomodoro();

        book.Tick(Monday9.AddHours(1));

        Assert.Equal(PomodoroPhase.Idle, book.Phase);
        Assert.Equal(0, book.PomodoroCompleted);
    }

    // ---------- 저장 ----------

    [Fact]
    public void SaveAndLoadKeepsTodosAndReminders()
    {
        var book = Started(Monday9);
        book.SetTodo(0, "배포");
        book.SetTodoDone(0, true, Monday9);
        book.Reminders.Add(new Reminder { Text = "회의", Hour = 14, Minute = 30 });
        book.StartPomodoro(Monday9);
        book.Tick(Monday9.AddMinutes(Balance.PomodoroFocusMinutes));

        var restored = Notebook.FromSave(book.ToSave());

        Assert.Equal("배포", restored.Todos[0].Text);
        Assert.True(restored.Todos[0].Done);
        Assert.Single(restored.Reminders);
        Assert.Equal("14:30", restored.Reminders[0].TimeLabel);
        Assert.Equal(1, restored.PomodoroCompleted);
    }

    /// <summary>
    /// 껐다 켰는데 "집중 중"이라고 우기면 남은 시간이 거짓말이 된다. 일부러 복원하지 않는다.
    /// </summary>
    [Fact]
    public void PomodoroDoesNotSurviveRestart()
    {
        var book = Started(Monday9);
        book.StartPomodoro(Monday9);

        var restored = Notebook.FromSave(book.ToSave());

        Assert.Equal(PomodoroPhase.Idle, restored.Phase);
    }

    [Fact]
    public void LoadingNothingGivesAnEmptyNotebook()
    {
        var book = Notebook.FromSave(null);

        Assert.Equal(3, book.Todos.Count);
        Assert.All(book.Todos, t => Assert.True(t.IsEmpty));
        Assert.Empty(book.Reminders);
    }
}
