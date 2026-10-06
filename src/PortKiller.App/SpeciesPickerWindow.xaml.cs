using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PortKiller.Core;

namespace PortKiller.App;

/// <summary>
/// 키울 동물을 고르는 창. 종마다 살아 움직이는 3D 미리보기와 태어나는 방식(알 / 새끼)을 보여 준다.
/// 고르면 <see cref="Chosen"/> 에 종 id 가 담기고, 그냥 닫으면 null 이다.
/// </summary>
public partial class SpeciesPickerWindow : Window
{
    private readonly List<AnimalViewBase> _previews = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };

    /// <summary>고른 종의 id. 고르지 않고 닫았으면 null.</summary>
    public string? Chosen { get; private set; }

    /// <param name="firstTime">처음 실행해서 고르는 경우(이전 펫이 없다).</param>
    public SpeciesPickerWindow(bool firstTime)
    {
        InitializeComponent();

        SubHeading.Text = firstTime
            ? "처음 만나는 친구를 골라 주세요. 종마다 태어나는 방식도, 움직이는 모습도 달라요."
            : "새 친구를 골라 주세요. 지금 키우는 아이와는 헤어지게 돼요.";
        Footer.Text = "알에서 태어나는 아이는 알을 쓰다듬어 깨우고, 새끼로 태어나는 아이는 바구니에서 자는 새끼를 쓰다듬어 깨워요.";

        foreach (var kind in PetKinds.All) Cards.Children.Add(BuildCard(kind));

        _timer.Tick += (_, _) =>
        {
            var t = _clock.Elapsed.TotalSeconds;
            foreach (var view in _previews) view.Animate(PetPose.Idle, false, t);
        };
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private UIElement BuildCard(PetKind kind)
    {
        AnimalViewBase view = kind.Rig == RigKind.Biped ? new BipedView() : new QuadrupedView();
        view.Width = view.Height = 150;
        view.Configure(kind.Id, "day", LifeStage.Adult);
        _previews.Add(view);

        var preview = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2F, 0x3A)),
            CornerRadius = new CornerRadius(10),
            Height = 150,
            Child = view,
            Margin = new Thickness(0, 0, 0, 10),
        };

        var birth = kind.Birth == BirthKind.Egg ? "🥚 알에서 태어나요" : "🧺 새끼로 태어나요";
        var pick = new Button
        {
            Content = "이 아이로 시작",
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 12, 0, 0),
            Tag = kind.Id,
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(pick, "Pick_" + kind.Id);
        pick.Click += (_, _) =>
        {
            Chosen = kind.Id;
            DialogResult = true;
        };

        var stack = new StackPanel();
        stack.Children.Add(preview);
        stack.Children.Add(new TextBlock { Text = kind.Name, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x2F, 0x4F, 0x2A)) });
        stack.Children.Add(new TextBlock { Text = birth, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x73, 0x99)), Margin = new Thickness(0, 2, 0, 0) });
        stack.Children.Add(new TextBlock { Text = kind.Blurb, FontSize = 12, TextWrapping = TextWrapping.Wrap, MinHeight = 34, Margin = new Thickness(0, 6, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x50)) });
        stack.Children.Add(pick);

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xDA)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Margin = new Thickness(5),
            Child = stack,
        };
    }
}
