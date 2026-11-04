using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PortKiller.Core;

namespace PortKiller.App;

/// <summary>
/// ALFA 연동 창(dot, §14). 세 상태: ① 미연동(REQUEST) ② 대기(코드·mm:ss·4단계·CANCEL) ③ 연결(CHECK·UNLINK).
///
/// <para>보여 주기만 한다. 요청·받아 가기·확인·끊기는 <see cref="AlfaLinkController"/> 가 펫 본체에서 한다 —
/// 이 창을 닫아도 대기는 계속된다(C1). 키와 받아 가기 비밀값은 이 창에 오지 않는다.</para>
/// </summary>
public partial class AlfaLinkWindow : Window
{
    private static readonly string[] StepNames = { "REQUEST", "CODE", "APPROVE", "KEY" };

    private readonly AlfaLinkController _link;
    private readonly Func<(string Name, string Species)> _pet;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<Border> _stepDots = new();
    private string? _message;

    internal AlfaLinkWindow(AlfaLinkController link, Func<(string Name, string Species)> pet)
    {
        InitializeComponent();
        _link = link;
        _pet = pet;
        Background = (Brush)FindResource("DotWindow");

        BuildSteps();
        DeviceBox.Text = link.DeviceName;
        AddressBox.Text = link.BaseUrl;

        _link.Changed += OnChanged;
        _link.BusyChanged += Render;
        _tick.Tick += (_, _) => RenderClock();
        Loaded += (_, _) =>
        {
            _tick.Start();
            Render();
        };
        Closed += (_, _) =>
        {
            _tick.Stop();
            _link.Changed -= OnChanged;
            _link.BusyChanged -= Render;
        };
    }

    private void OnChanged(PetLinkState before, PetLinkState after)
    {
        // 단계가 바뀌면 앞 안내는 낡았다. 끝난 이유는 상태가 말한다.
        if (before.Phase != after.Phase) _message = null;
        Render();
    }

    private void BuildSteps()
    {
        for (var i = 0; i < StepNames.Length; i++)
        {
            var dot = new Border
            {
                Height = 8,
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(i == 0 ? 0 : 3, 0, i == StepNames.Length - 1 ? 0 : 3, 4),
            };
            var label = new TextBlock
            {
                Text = StepNames[i],
                Style = (Style)FindResource("DotLabelText"),
                Margin = new Thickness(0),
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var cell = new StackPanel();
            cell.Children.Add(dot);
            cell.Children.Add(label);
            Steps.Children.Add(cell);
            _stepDots.Add(dot);
        }
    }

    private void Render()
    {
        var s = _link.State;

        StatusText.Text = $"{s.Glyph} {s.StatusWord}";
        StatusText.Foreground = (Brush)FindResource(s.Phase switch
        {
            PetLinkPhase.Linked => "DotGold",
            PetLinkPhase.Waiting => "DotGoldLight",
            PetLinkPhase.Rejected or PetLinkPhase.Revoked or PetLinkPhase.KeyLost => "DotWarn",
            _ => "DotMuted",
        });

        OffPanel.Visibility = !s.IsWaiting && !s.IsLinked ? Visibility.Visible : Visibility.Collapsed;
        WaitingPanel.Visibility = s.IsWaiting ? Visibility.Visible : Visibility.Collapsed;
        LinkedPanel.Visibility = s.IsLinked ? Visibility.Visible : Visibility.Collapsed;
        ClockText.Visibility = s.IsWaiting ? Visibility.Visible : Visibility.Collapsed;

        var pet = _pet();
        PetText.Text = $"{pet.Name} · {pet.Species}";
        CodeText.Text = s.Code ?? "------";
        LinkedDeviceText.Text = _link.DeviceName;
        LinkedAtText.Text = s.LinkedAt is { } at ? at.ToLocalTime().ToString("MM-dd HH:mm") : "—";

        RequestButton.IsEnabled = !_link.Busy;
        DeviceBox.IsEnabled = AddressBox.IsEnabled = !_link.Busy;
        CheckButton.IsEnabled = UnlinkButton.IsEnabled = !_link.Busy;
        RequestButton.Content = _link.Busy ? "REQUESTING · 요청 중…" : "▦ REQUEST · 연동 요청 ▦";

        RenderSteps(s);
        RenderClock();

        // 끝난 이유는 늘 한 줄로 보인다. C9: 옛 키가 401 이면 조용히 실패하지 않고 "끊겼어요".
        var line = _message ?? s.Phase switch
        {
            PetLinkPhase.Revoked => "ALFA 연결이 끊겼어요. 다시 요청해 주세요.",
            PetLinkPhase.Expired => "승인 시간 10분이 지났어요. 다시 요청할까요?",
            PetLinkPhase.Rejected => "연동이 안 됐어요. 다시 요청할까요?",
            PetLinkPhase.KeyLost => "승인은 됐는데 키를 받는 중에 연결이 끊겨 키를 놓쳤어요. 다시 요청해 주세요(ALFA 설정에서 앞 연동은 해제해도 돼요).",
            PetLinkPhase.Unreachable => "ALFA 에 닿지 않아요. 다음 확인 때 다시 볼게요.",
            _ => null,
        };
        MessageText.Text = line ?? "";
        MessageText.Visibility = line is null ? Visibility.Collapsed : Visibility.Visible;
        MessageText.Foreground = (Brush)FindResource(s.Phase == PetLinkPhase.Unreachable && _message is null ? "DotMuted" : "DotWarn");
    }

    /// <summary>요청·코드는 채움, 승인은 깜빡(움직임 줄이기면 정지), 키는 중립. 연결되면 전부 채움.</summary>
    private void RenderSteps(PetLinkState s)
    {
        var filled = (Brush)FindResource("DotGoldFill");
        var neutral = (Brush)FindResource("DotNeutralFill");
        var reduceMotion = !SystemParameters.ClientAreaAnimation;
        for (var i = 0; i < _stepDots.Count; i++)
        {
            var dot = _stepDots[i];
            var active = s.IsWaiting && i == 2;
            dot.Background = s.IsLinked || (s.IsWaiting && i < 2) || active ? filled : neutral;
            if (active && !reduceMotion)
            {
                if (dot.Tag is null)
                {
                    dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromSeconds(0.9))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                    });
                    dot.Tag = "blink";
                }
            }
            else if (dot.Tag is not null)
            {
                dot.BeginAnimation(OpacityProperty, null);
                dot.Opacity = 1;
                dot.Tag = null;
            }
        }
    }

    private void RenderClock()
    {
        var s = _link.State;
        var now = DateTimeOffset.UtcNow;
        if (s.IsWaiting) ClockText.Text = PetLinkState.Clock(s.Remaining(now));
        LastCheckText.Text = s.LastSeenMinutes(now) is { } m ? (m == 0 ? "방금" : PetLinkState.Ago(m) + " 전") : "—";
    }

    private void ShowMessage(string? message)
    {
        _message = message;
        Render();
    }

    private async void RequestButton_Click(object sender, RoutedEventArgs e)
    {
        var pet = _pet();
        _message = null;
        var error = await _link.RequestAsync(DeviceBox.Text, AddressBox.Text, pet.Name, pet.Species);
        if (!IsLoaded) return;
        if (error is null) DeviceBox.Text = _link.DeviceName;
        ShowMessage(error);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _link.Cancel();

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        // 확인 코드는 비밀이 아니다(ALFA 화면에도 같은 코드가 보인다). 받아 가기 비밀값은 여기 없다.
        if (_link.State.Code is not { } code) return;
        try { Clipboard.SetText(code); } catch (Exception) { }
    }

    private async void CheckButton_Click(object sender, RoutedEventArgs e)
    {
        _message = null;
        await _link.CheckAsync();
        if (IsLoaded) Render();
    }

    private async void UnlinkButton_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "ALFA 연동을 끊을까요?\n이 펫의 키는 바로 쓸 수 없게 되고, 다시 쓰려면 새로 연동해야 해요.",
            "UNLINK · 연동 끊기",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;

        var error = await _link.UnlinkAsync();
        if (IsLoaded) ShowMessage(error);
    }
}
