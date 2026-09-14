namespace PortKiller.Core;

public enum PomodoroPhase
{
    Idle,
    Focus,
    Break,
}

/// <summary>리마인더를 언제 다시 울릴지.</summary>
public enum ReminderRepeat
{
    Once,
    Daily,
    Weekdays,
}

public sealed class TodoItem
{
    public string Text { get; set; } = "";
    public bool Done { get; set; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);
}

public sealed class Reminder
{
    public string Text { get; set; } = "";

    /// <summary>울릴 시각. 날짜는 쓰지 않고 시·분만 본다.</summary>
    public int Hour { get; set; }
    public int Minute { get; set; }

    public ReminderRepeat Repeat { get; set; } = ReminderRepeat.Once;
    public bool Enabled { get; set; } = true;

    /// <summary>마지막으로 울린 날(yyyy-MM-dd). 같은 날 두 번 울리는 것을 막는다.</summary>
    public string LastFiredOn { get; set; } = "";

    public string TimeLabel => $"{Hour:D2}:{Minute:D2}";
}

/// <summary>
/// 수첩 — 오늘 할 일, 리마인더, 뽀모도로.
///
/// 펫과 마찬가지로 화면을 모른다. 시각만 받아서 "지금 무슨 일이 일어나야 하는지"를 정하고
/// 이벤트로 알린다. 그래서 시계를 돌리지 않고도 전부 시험할 수 있다.
/// </summary>
public sealed class Notebook
{
    /// <summary>
    /// 할 일 칸 수는 3으로 고정한다.
    /// 늘릴 수 있게 두면 수첩이 할 일 관리 앱이 되어 버린다. 이건 곁다리 기능이다.
    /// </summary>
    public const int TodoSlots = 3;

    private readonly TodoItem[] _todos =
        Enumerable.Range(0, TodoSlots).Select(_ => new TodoItem()).ToArray();

    public IReadOnlyList<TodoItem> Todos => _todos;
    public List<Reminder> Reminders { get; } = new();

    public PomodoroPhase Phase { get; private set; } = PomodoroPhase.Idle;
    public int PomodoroCompleted { get; private set; }

    /// <summary>현재 구간이 끝나는 시각. 쉬는 중이든 집중 중이든 같은 칸을 쓴다.</summary>
    public DateTimeOffset? PhaseEndsAt { get; private set; }

    /// <summary>할 일이 언제 것인지. 날짜가 바뀌면 비운다.</summary>
    public string TodosDate { get; private set; } = "";

    public event Action<Reminder>? ReminderDue;
    public event Action<PomodoroPhase>? PhaseChanged;

    /// <summary>세 칸을 다 채우고 다 끝냈을 때 한 번. 펫이 축하한다.</summary>
    public event Action? AllTodosDone;

    // ---------- 시간 진행 ----------

    public void Tick(DateTimeOffset now)
    {
        ResetTodosIfNewDay(now);
        FireDueReminders(now);
        AdvancePomodoro(now);
    }

    /// <summary>"오늘 할 일"이므로 날이 바뀌면 비운다.</summary>
    private void ResetTodosIfNewDay(DateTimeOffset now)
    {
        var today = Today(now);
        if (TodosDate == today) return;

        TodosDate = today;
        foreach (var todo in _todos)
        {
            todo.Text = "";
            todo.Done = false;
        }
    }

    private void FireDueReminders(DateTimeOffset now)
    {
        var today = Today(now);

        foreach (var reminder in Reminders)
        {
            if (!reminder.Enabled) continue;
            if (reminder.LastFiredOn == today) continue;
            if (!MatchesToday(reminder, now)) continue;

            // 시각이 지났으면 울린다. 앱이 꺼져 있다 켜진 경우에도 그날 것은 한 번 알려준다.
            var dueMinutes = reminder.Hour * 60 + reminder.Minute;
            var nowMinutes = now.Hour * 60 + now.Minute;
            if (nowMinutes < dueMinutes) continue;

            reminder.LastFiredOn = today;
            if (reminder.Repeat == ReminderRepeat.Once) reminder.Enabled = false;

            ReminderDue?.Invoke(reminder);
        }
    }

    private static bool MatchesToday(Reminder reminder, DateTimeOffset now) => reminder.Repeat switch
    {
        ReminderRepeat.Daily => true,
        ReminderRepeat.Once => true,
        ReminderRepeat.Weekdays => now.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday),
        _ => false,
    };

    // ---------- 할 일 ----------

    public void SetTodo(int index, string text)
    {
        if (index < 0 || index >= TodoSlots) return;
        _todos[index].Text = text ?? "";
        if (_todos[index].IsEmpty) _todos[index].Done = false;
    }

    /// <summary>체크를 뒤집는다. 빈 칸은 체크할 수 없다.</summary>
    public void SetTodoDone(int index, bool done, DateTimeOffset now)
    {
        if (index < 0 || index >= TodoSlots) return;
        if (_todos[index].IsEmpty) return;

        var was = AllDone();
        _todos[index].Done = done;
        if (!was && AllDone()) AllTodosDone?.Invoke();
    }

    /// <summary>세 칸을 다 쓰고 다 끝냈을 때만 참. 한 칸만 쓰고 끝내는 것은 완주가 아니다.</summary>
    public bool AllDone() => _todos.All(t => !t.IsEmpty && t.Done);

    public int DoneCount() => _todos.Count(t => !t.IsEmpty && t.Done);
    public int FilledCount() => _todos.Count(t => !t.IsEmpty);

    // ---------- 뽀모도로 ----------

    public void StartPomodoro(DateTimeOffset now) => EnterPhase(PomodoroPhase.Focus, now);

    public void StopPomodoro()
    {
        if (Phase == PomodoroPhase.Idle) return;
        Phase = PomodoroPhase.Idle;
        PhaseEndsAt = null;
        PhaseChanged?.Invoke(Phase);
    }

    /// <summary>남은 시간. 안 돌고 있으면 0.</summary>
    public TimeSpan Remaining(DateTimeOffset now)
    {
        if (PhaseEndsAt is null) return TimeSpan.Zero;
        var left = PhaseEndsAt.Value - now;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    private void AdvancePomodoro(DateTimeOffset now)
    {
        if (Phase == PomodoroPhase.Idle || PhaseEndsAt is null) return;
        if (now < PhaseEndsAt.Value) return;

        if (Phase == PomodoroPhase.Focus)
        {
            PomodoroCompleted++;
            EnterPhase(PomodoroPhase.Break, now);
        }
        else
        {
            StopPomodoro();
        }
    }

    private void EnterPhase(PomodoroPhase phase, DateTimeOffset now)
    {
        Phase = phase;
        PhaseEndsAt = phase switch
        {
            PomodoroPhase.Focus => now.AddMinutes(Balance.PomodoroFocusMinutes),
            PomodoroPhase.Break => now.AddMinutes(Balance.PomodoroBreakMinutes),
            _ => null,
        };
        PhaseChanged?.Invoke(phase);
    }

    private static string Today(DateTimeOffset now) => now.ToString("yyyy-MM-dd");

    // ---------- 저장/복원 ----------

    public NotebookData ToSave() => new()
    {
        TodosDate = TodosDate,
        TodoTexts = _todos.Select(t => t.Text).ToList(),
        TodoDone = _todos.Select(t => t.Done).ToList(),
        Reminders = Reminders.ToList(),
        PomodoroCompleted = PomodoroCompleted,
    };

    public static Notebook FromSave(NotebookData? data)
    {
        var book = new Notebook();
        if (data is null) return book;

        book.TodosDate = data.TodosDate;
        for (var i = 0; i < TodoSlots; i++)
        {
            if (i < data.TodoTexts.Count) book._todos[i].Text = data.TodoTexts[i];
            if (i < data.TodoDone.Count) book._todos[i].Done = data.TodoDone[i];
        }
        book.Reminders.AddRange(data.Reminders);
        book.PomodoroCompleted = data.PomodoroCompleted;

        // 뽀모도로는 일부러 복원하지 않는다.
        // 앱을 껐다 켠 뒤에도 "집중 중"이라고 우기면 남은 시간이 거짓말이 된다.
        return book;
    }
}

public sealed class NotebookData
{
    public string TodosDate { get; set; } = "";
    public List<string> TodoTexts { get; set; } = new();
    public List<bool> TodoDone { get; set; } = new();
    public List<Reminder> Reminders { get; set; } = new();
    public int PomodoroCompleted { get; set; }
}
