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
            Bars.Children.Add(grid);
            _rows.Add((fill, value));
        }
    }

    public void Update(Pet pet, bool userAway)
    {
        TitleText.Text = TitleFor(pet);
        SubText.Text = pet.Stage == LifeStage.Egg ? "곧 태어날 것 같아요" : SpeciesName(pet.SpeciesId);
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
                GrowthLabel.Text = "부화까지 (쓰다듬기)";
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
            { Stage: LifeStage.Egg } => ("🥚 알", "#FF6B7399"),
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
            LifeStage.Egg => "알",
            LifeStage.Baby => "아기",
            LifeStage.Child => "소년기",
            _ => "성체",
        };
        return pet.Stage == LifeStage.Adult && pet.EvolutionTier > 0
            ? $"{stage} {new string('★', pet.EvolutionTier)}"
            : stage;
    }

    private static string SpeciesName(string id) => id switch
    {
        "" => "-",
        "day" => "낮의 레서판다",
        "evening" => "저녁의 레서판다",
        _ => id,
    };

    private static Brush Hex(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }
}
