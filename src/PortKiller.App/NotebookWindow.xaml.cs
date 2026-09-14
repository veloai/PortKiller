using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PortKiller.Core;

namespace PortKiller.App;

/// <summary>
/// 수첩 창. 규칙은 전부 Core 의 <see cref="Notebook"/> 에 있고, 여기는 보여주고 눌러 넘기기만 한다.
///
/// 창을 닫아도 수첩은 살아 있다. 펫이 계속 리마인더를 울려야 하므로
/// 이 창은 Notebook 을 소유하지 않고 빌려 쓴다.
/// </summary>
public partial class NotebookWindow : Window
{
    private readonly Notebook _book;
    private readonly List<(CheckBox Check, TextBox Text)> _todoRows = new();
    private readonly DispatcherTimer _clock;

    private bool _loading;

    public NotebookWindow(Notebook book)
    {
        InitializeComponent();
        _book = book;

        BuildTodoRows();
        RefreshReminders();
        RefreshPomodoro();

        // 남은 시간을 보여줘야 하므로 창이 열려 있는 동안만 초마다 갱신한다.
        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => RefreshPomodoro();
        _clock.Start();

        Closed += (_, _) => _clock.Stop();
    }

    // ---------- 오늘 할 일 ----------

    private void BuildTodoRows()
    {
        _loading = true;

        for (var i = 0; i < Notebook.TodoSlots; i++)
        {
            var index = i;
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var check = new CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                IsChecked = _book.Todos[i].Done,
            };
            check.Checked += (_, _) => OnTodoChecked(index, true);
            check.Unchecked += (_, _) => OnTodoChecked(index, false);

            var text = new TextBox
            {
                Text = _book.Todos[i].Text,
                Padding = new Thickness(6, 4, 6, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                Tag = $"할 일 {index + 1}",
            };
            text.TextChanged += (_, _) => OnTodoTextChanged(index, text.Text);

            Grid.SetColumn(check, 0);
            Grid.SetColumn(text, 1);
            row.Children.Add(check);
            row.Children.Add(text);

            TodoRows.Children.Add(row);
            _todoRows.Add((check, text));
        }

        _loading = false;
        RefreshTodoHint();
    }

    private void OnTodoChecked(int index, bool done)
    {
        if (_loading) return;
        _book.SetTodoDone(index, done, DateTimeOffset.Now);

        // 빈 칸은 체크할 수 없다. 규칙이 거절했으면 화면도 되돌려서 거짓말하지 않게 한다.
        var actual = _book.Todos[index].Done;
        if (_todoRows[index].Check.IsChecked != actual)
        {
            _loading = true;
            _todoRows[index].Check.IsChecked = actual;
            _loading = false;
        }

        RefreshTodoHint();
    }

    private void OnTodoTextChanged(int index, string text)
    {
        if (_loading) return;
        _book.SetTodo(index, text);

        if (_book.Todos[index].IsEmpty && _todoRows[index].Check.IsChecked == true)
        {
            _loading = true;
            _todoRows[index].Check.IsChecked = false;
            _loading = false;
        }

        RefreshTodoHint();
    }

    private void RefreshTodoHint()
        => TodoHint.Text = _book.AllDone()
            ? "오늘 할 일 완료! 🎉"
            : $"{_book.DoneCount()} / {Notebook.TodoSlots} 완료 — 세 개를 다 끝내면 펫이 축하해줘요";

    // ---------- 리마인더 ----------

    private void RefreshReminders()
    {
        ReminderList.Items.Clear();

        foreach (var reminder in _book.Reminders)
        {
            var current = reminder;
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var when = new TextBlock
            {
                Text = $"{current.TimeLabel}  {RepeatLabel(current.Repeat)}",
                Width = 96,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = current.Enabled
                    ? System.Windows.Media.Brushes.DimGray
                    : System.Windows.Media.Brushes.Silver,
            };

            var what = new TextBlock
            {
                Text = current.Text,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = current.Enabled
                    ? System.Windows.Media.Brushes.Black
                    : System.Windows.Media.Brushes.Silver,
            };

            var remove = new Button { Content = "×", Width = 24, Padding = new Thickness(0) };
            remove.Click += (_, _) =>
            {
                _book.Reminders.Remove(current);
                RefreshReminders();
            };

            Grid.SetColumn(when, 0);
            Grid.SetColumn(what, 1);
            Grid.SetColumn(remove, 2);
            row.Children.Add(when);
            row.Children.Add(what);
            row.Children.Add(remove);

            ReminderList.Items.Add(row);
        }

        ReminderEmpty.Visibility = _book.Reminders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string RepeatLabel(ReminderRepeat repeat) => repeat switch
    {
        ReminderRepeat.Daily => "매일",
        ReminderRepeat.Weekdays => "평일",
        _ => "한 번",
    };

    private void AddReminder_Click(object sender, RoutedEventArgs e)
    {
        ReminderError.Visibility = Visibility.Collapsed;

        var text = ReminderText.Text.Trim();
        if (text.Length == 0)
        {
            ShowReminderError("알림 내용을 적어주세요.");
            return;
        }

        if (!TryParseTime(ReminderTime.Text, out var hour, out var minute))
        {
            ShowReminderError("시각은 14:00 처럼 적어주세요.");
            return;
        }

        _book.Reminders.Add(new Reminder
        {
            Text = text,
            Hour = hour,
            Minute = minute,
            Repeat = (ReminderRepeat)Math.Max(0, ReminderRepeatBox.SelectedIndex),
            // 이미 지난 시각으로 등록했다면 오늘은 넘기고 다음부터 울린다.
            // 등록하자마자 울리면 방금 적은 알림이 바로 사라진 것처럼 보인다.
            LastFiredOn = IsPastToday(hour, minute) ? DateTimeOffset.Now.ToString("yyyy-MM-dd") : "",
        });

        ReminderText.Clear();
        RefreshReminders();
    }

    private static bool IsPastToday(int hour, int minute)
    {
        var now = DateTimeOffset.Now;
        return hour * 60 + minute <= now.Hour * 60 + now.Minute;
    }

    /// <summary>"14:00", "1400", "9:5" 같은 입력을 받아준다. 못 읽으면 false.</summary>
    private static bool TryParseTime(string input, out int hour, out int minute)
    {
        hour = minute = 0;
        var raw = (input ?? "").Trim();
        if (raw.Length == 0) return false;

        var parts = raw.Split(':', '.', ' ');
        if (parts.Length == 1 && raw.Length == 4 && raw.All(char.IsDigit))
            parts = new[] { raw[..2], raw[2..] };
        if (parts.Length != 2) return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hour)) return false;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minute)) return false;

        return hour is >= 0 and <= 23 && minute is >= 0 and <= 59;
    }

    private void ShowReminderError(string message)
    {
        ReminderError.Text = message;
        ReminderError.Visibility = Visibility.Visible;
    }

    // ---------- 뽀모도로 ----------

    private void PomodoroButton_Click(object sender, RoutedEventArgs e)
    {
        if (_book.Phase == PomodoroPhase.Idle) _book.StartPomodoro(DateTimeOffset.Now);
        else _book.StopPomodoro();

        RefreshPomodoro();
    }

    private void RefreshPomodoro()
    {
        var remaining = _book.Remaining(DateTimeOffset.Now);

        PomodoroPhaseText.Text = _book.Phase switch
        {
            PomodoroPhase.Focus => "집중 중 — 펫도 조용히 응원할게",
            PomodoroPhase.Break => "쉬는 시간",
            _ => "시작을 누르면 25분 집중이 시작돼요",
        };

        PomodoroClock.Text = _book.Phase == PomodoroPhase.Idle
            ? $"{Balance.PomodoroFocusMinutes:00}:00"
            : $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";

        PomodoroButton.Content = _book.Phase == PomodoroPhase.Idle ? "집중 시작" : "그만두기";
        PomodoroCount.Text = $"지금까지 {_book.PomodoroCompleted}회";
    }
}
