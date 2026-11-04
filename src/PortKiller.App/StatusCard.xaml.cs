using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PortKiller.Core;

namespace PortKiller.App;

/// <summary>
/// 펫 상태 카드. 화면에 그리기만 하고 게임 규칙은 모른다 — 값은 <see cref="Pet"/> 에서 읽어 온다.
///
/// <para>수치 막대는 색으로 위험을 알린다. 초록 &gt; 50, 노랑 25~50, 빨강 &lt; 25.
/// 기준을 게임 규칙(<see cref="Balance"/>)에서 따로 가져오지 않은 것은 "보기 좋은 경고선"이라서다 —
/// 아프거나 삐지는 실제 경계는 기분 칩이 말해 준다.</para>
/// </summary>
public partial class StatusCard : UserControl
{
    private const double BarWidth = 120;

    private static readonly Brush Good = Hex("#FF6FD08C");
    private static readonly Brush Warn = Hex("#FFF2B84B");
    private static readonly Brush Bad = Hex("#FFEF5F6B");

    private readonly (string Label, Func<Pet, double> Read)[] _defs =
    {
        ("🍚 배부름", p => p.Stats.Hunger),
        ("💛 행복", p => p.Stats.Happiness),
        ("⚡ 기력", p => p.Stats.Energy),
        ("❤ 체력", p => p.Stats.Health),
        ("🧼 청결", p => p.Stats.Cleanliness),
    };

    private readonly List<(Border Fill, TextBlock Value)> _rows = new();
    private readonly List<Grid> _rowGrids = new();
    private static readonly BriefingTopic[] Topics =
    {
        BriefingTopic.Hunger, BriefingTopic.Happiness, BriefingTopic.Energy,
        BriefingTopic.Health, BriefingTopic.Cleanliness,
    };

    private Pet? _pet;
    private int _hover = -1;

    /// <summary>개발 환경 확인 결과와 그 줄들. 마우스로 짚을 수 있게 수치 막대 뒤에 이어 센다.</summary>
    private IReadOnlyList<DevCheck> _dev = Array.Empty<DevCheck>();
    private readonly List<Grid> _devGrids = new();

    private IEnumerable<Grid> AllRows => _rowGrids.Concat(_devGrids);

    /// <summary>✕ 를 눌렀다. 브리핑을 끝내 달라는 뜻이다.</summary>
    public event Action? CloseRequested;

    private void Close_Clicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        CloseRequested?.Invoke();
        e.Handled = true;
    }

    public StatusCard()
    {
        InitializeComponent();
        BuildRows();
    }

    private void BuildRows()
    {
        foreach (var def in _defs)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

            var label = new TextBlock { Text = def.Label, Foreground = Hex("#FFCBD2F0"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var track = new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = Hex("#FF2B3050"), VerticalAlignment = VerticalAlignment.Center };
            var fill = new Border { CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            track.Child = fill;
            var value = new TextBlock { Foreground = Brushes.White, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };

            Grid.SetColumn(track, 1);
            Grid.SetColumn(value, 2);
            grid.Children.Add(label);
            grid.Children.Add(track);
            grid.Children.Add(value);
            grid.Background = Brushes.Transparent;   // 비어 있는 곳도 가리킬 수 있게 하고, 강조색이 들어갈 자리
            Bars.Children.Add(grid);
            _rowGrids.Add(grid);
            _rows.Add((fill, value));
        }
    }

    /// <summary>
    /// 마우스가 카드 위 <paramref name="p"/>(카드 좌표)에 있다. 그 자리의 항목을 강조하고 레서판다가 한마디 한다.
    /// 항목이 아닌 곳이면 전체 요약을 말한다.
    /// </summary>
    public void Pointer(Point p)
    {
        var hover = -1;
        var rows = AllRows.ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            var g = rows[i];
            if (g.ActualHeight <= 0) continue;
            var top = g.TranslatePoint(new Point(0, 0), this).Y - 3;
            if (p.Y >= top && p.Y <= top + g.ActualHeight + 6) { hover = i; break; }
        }

        if (hover == _hover) return;
        _hover = hover;
        Highlight();
        RefreshCaption();
    }

    /// <summary>마우스가 카드를 떠났다.</summary>
    public void ClearPointer()
    {
        _hover = -1;
        Highlight();
        RefreshCaption();
    }

    private void Highlight()
    {
        var rows = AllRows.ToList();
        for (var i = 0; i < rows.Count; i++)
            rows[i].Background = i == _hover ? Hex("#33FFFFFF") : Brushes.Transparent;
    }

    private void RefreshCaption()
    {
        if (_pet is null) return;
        if (_hover >= 0 && _hover < _defs.Length)
        {
            Caption.Text = Briefing.Line(Topics[_hover], _defs[_hover].Read(_pet));
            return;
        }

        var devIndex = _hover - _defs.Length;
        if (_hover >= 0 && devIndex < _dev.Count)
        {
            Caption.Text = DevStatusRules.Describe(_dev[devIndex]);
            return;
        }

        // 아무것도 안 짚었을 때: 개발 환경에 문제가 있으면 그게 먼저다.
        Caption.Text = _dev.Any(c => c.State == DevState.Down)
            ? DevStatusRules.Summary(_dev)
            : Briefing.Summary(_pet.Stats);
    }

    /// <summary>펫의 기억 한 줄(이번 주 요약). 빈 값이면 숨긴다.</summary>
    public void UpdateMemory(string? line)
    {
        MemoryText.Text = line is { Length: > 0 } ? "🧠 " + line : string.Empty;
        MemoryText.Visibility = MemoryText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>개발 환경 확인 결과를 카드에 그린다(1분에 한 번 정도 불린다).</summary>
    /// <summary>ALFA 연동 한 줄. null 이면 숨긴다.</summary>
    public void UpdateAlfa(string? line)
    {
        if (AlfaText.Text == (line ?? string.Empty) && (line is null) == (AlfaText.Visibility == Visibility.Collapsed)) return;
        AlfaText.Text = line ?? string.Empty;
        AlfaText.Visibility = line is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public void UpdateDev(IReadOnlyList<DevCheck> checks)
    {
        _dev = checks;
        DevRows.Children.Clear();
        _devGrids.Clear();

        foreach (var c in checks)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 3), Background = Brushes.Transparent };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var color = c.State switch { DevState.Ok => Good, DevState.Down => Bad, _ => Hex("#FF6B7399") };
            var dot = new TextBlock { Text = "●", Foreground = color, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock { Text = c.Name, Foreground = Hex("#FFCBD2F0"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            var state = new TextBlock
            {
                Text = c.State switch { DevState.Ok => "정상", DevState.Down => "멈춤", _ => "확인 불가" },
                Foreground = color,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };

            Grid.SetColumn(name, 1);
            Grid.SetColumn(state, 2);
            grid.Children.Add(dot);
            grid.Children.Add(name);
            grid.Children.Add(state);
            DevRows.Children.Add(grid);
            _devGrids.Add(grid);
        }

        DevBox.Visibility = checks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Highlight();
        RefreshCaption();
    }

    public void Update(Pet pet, bool userAway)
    {
        _pet = pet;
        TitleText.Text = TitleFor(pet);
        SubText.Text = pet.Stage == LifeStage.Egg
            ? (pet.KindInfo.Birth == BirthKind.Egg ? "곧 태어날 것 같아요" : "새근새근 자고 있어요")
            : SpeciesName(pet);
        ApplyMood(pet, userAway);

        var eggStage = pet.Stage == LifeStage.Egg;
        Bars.Visibility = eggStage ? Visibility.Collapsed : Visibility.Visible;
        for (var i = 0; i < _defs.Length; i++)
        {
            var v = _defs[i].Read(pet);
            _rows[i].Fill.Width = BarWidth * v / 100.0;
            _rows[i].Fill.Background = v >= 50 ? Good : v >= 25 ? Warn : Bad;
            _rows[i].Value.Text = v.ToString("F0");
        }

        UpdateGrowth(pet);
        RefreshCaption();

        FocusText.Text = $"⏱ {pet.FocusSeconds / 3600.0:F1}h";
        DaysText.Text = $"📅 {pet.DistinctActiveDays}일";
        PoopText.Text = $"💩 {pet.PoopsOnGround}";
    }

    private void UpdateGrowth(Pet pet)
    {
        double ratio;
        switch (pet.Stage)
        {
            case LifeStage.Egg:
                GrowthLabel.Text = pet.KindInfo.Birth == BirthKind.Egg ? "부화까지 (쓰다듬기)" : "깨어나기까지 (쓰다듬기)";
                GrowthValue.Text = $"{pet.HatchClicks}/{Balance.HatchClicksRequired}";
                ratio = (double)pet.HatchClicks / Balance.HatchClicksRequired;
                break;
            case LifeStage.Adult when pet.EvolutionTier >= 2:
                GrowthLabel.Text = "최종 진화 완료";
                GrowthValue.Text = "★★";
                ratio = 1;
                break;
            case LifeStage.Adult:
                var hours = pet.FocusSeconds / 3600.0;
                var need = pet.EvolutionTier == 0 ? Balance.EvolutionFocusHours : Balance.EvolutionFocusHours * 2;
                var second = pet.EvolutionTier == 0
                    ? (double)pet.DistinctActiveDays / Balance.EvolutionDistinctDays
                    : (double)pet.CareCount / Balance.EvolutionCareCount;
                ratio = (Math.Min(1, hours / need) + Math.Min(1, second)) / 2;
                GrowthLabel.Text = "다음 진화까지";
                GrowthValue.Text = $"{ratio * 100:F0}%";
                break;
            default:
                GrowthLabel.Text = pet.Stage == LifeStage.Baby ? "소년기까지" : "성체까지";
                GrowthValue.Text = string.Empty;
                ratio = 0;
                break;
        }

        // 막대 폭은 카드 안쪽 폭에서 잰다. 부모가 아직 배치되기 전(0)이면 다음 갱신에 맞춘다.
        var full = GrowthBox.ActualWidth;
        GrowthFill.Width = Math.Max(0, Math.Min(1, ratio)) * (full > 0 ? full : 216);
        GrowthBox.Visibility = pet.Stage is LifeStage.Baby or LifeStage.Child && GrowthValue.Text.Length == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ApplyMood(Pet pet, bool userAway)
    {
        (string text, string color) = pet switch
        {
            { Stage: LifeStage.Egg } => (pet.KindInfo.Birth == BirthKind.Egg ? "🥚 알" : "🧺 새끼", "#FF6B7399"),
            { IsAsleep: true } => ("😴 자는 중", "#FF5B6BC0"),
            { IsSick: true } => ("🤒 아파요", "#FFC0505A"),
            { IsSulking: true } => ("😤 삐졌어요", "#FFB86B3A"),
            _ when pet.Stats.Hunger < Balance.HungerWarnThreshold => ("🍚 배고파요", "#FFC0803A"),
            _ when userAway => ("💤 기다리는 중", "#FF5C6488"),
            _ => ("😊 좋아요", "#FF3E9C62"),
        };
        MoodText.Text = text;
        MoodChip.Background = Hex(color);
    }

    private static string TitleFor(Pet pet)
    {
        var stage = pet.Stage switch
        {
            LifeStage.Egg => pet.KindInfo.NewbornNoun,
            LifeStage.Baby => "아기",
            LifeStage.Child => "소년기",
            _ => "성체",
        };
        return pet.Stage == LifeStage.Adult && pet.EvolutionTier > 0
            ? $"{stage} {new string('★', pet.EvolutionTier)}"
            : stage;
    }

    /// <summary>"낮의 고양이" 처럼 색 변형(언제 깨어났나)과 종 이름을 합친 말.</summary>
    private static string SpeciesName(Pet pet)
        => $"{(pet.SpeciesId == "evening" ? "저녁의" : "낮의")} {pet.KindInfo.Name}";

    private static Brush Hex(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }
}
