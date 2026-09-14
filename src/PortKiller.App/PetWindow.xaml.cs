using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PortKiller.Core;
using PortKiller.Platform;

namespace PortKiller.App;

public partial class PetWindow : Window
{
    /// <summary>펫이 밟고 설 바닥을 화면 아래에서 이만큼 띄운다.</summary>
    private const double GroundMargin = 8.0;

    /// <summary>이 거리 안에서 손을 떼면 "끌었다"가 아니라 "눌렀다"로 본다.</summary>
    private const double DragThreshold = 4.0;

    private readonly ISaveStore _store = new JsonSaveStore();
    private readonly ActivityMonitor _activity = new();
    private readonly Random _rng = new();
    private readonly List<FrameworkElement> _poops = new();

    private Pet _pet = new();
    private PetMotion _motion = null!;
    private TrayIcon? _tray;
    private DispatcherTimer? _gameLoop;
    private DateTimeOffset _lastTick = DateTimeOffset.Now;
    private DateTimeOffset _lastTooltipUpdate = DateTimeOffset.MinValue;

    private Point _pressedAt;
    private bool _dragging;
    private DateTimeOffset _bubbleHideAt = DateTimeOffset.MinValue;

    public PetWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
    }

    /// <summary>
    /// 클릭 통과는 코드로 하는 일이 없다.
    /// 이 창은 투명 합성 창(AllowsTransparency)이라, 완전히 투명한 픽셀은
    /// 윈도우가 알아서 "여기 창 없음"으로 처리해 뒤 창으로 클릭을 넘긴다.
    /// 그림이 그려진 자리만 클릭을 받는다 — 사각형이 아니라 그림 모양 그대로.
    /// (실측 확인: docs/verification/clickthrough.md)
    /// 그래서 마우스를 감시하거나 통과 영역을 계산하는 코드가 필요 없다.
    /// </summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
        => NativeMethods.MarkAsToolWindow(new WindowInteropHelper(this).Handle);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CoverPrimaryScreen();

        _motion = new PetMotion(PetBody.Width, PetBody.Height, _rng);
        _motion.PlaceOnGround(Width * 0.5 - PetBody.Width * 0.5, Bounds());

        Canvas.SetLeft(DebugPanel, 20);
        Canvas.SetTop(DebugPanel, 20);

        LoadPet();
        SetUpTray();
        StartGameLoop();
    }

    // ---------- 트레이 ----------

    /// <summary>
    /// NotifyIcon 은 메시지 펌프가 있는 스레드에서 만들어야 한다.
    /// 여기(UI 스레드)에서 만들면 이벤트도 UI 스레드로 올라와 창을 바로 만질 수 있다.
    /// </summary>
    private void SetUpTray()
    {
        _tray = new TrayIcon();
        _tray.StatsRequested += () => DebugPanel.Visibility =
            DebugPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        _tray.AlwaysOnTopToggled += on => Topmost = on;
        _tray.ResetRequested += ConfirmAndReset;
        _tray.QuitRequested += Close;
    }

    private void ConfirmAndReset()
    {
        var answer = MessageBox.Show(
            "정말 처음부터 다시 키울까요?\n지금 펫과는 영영 헤어지게 돼요…",
            "처음부터 다시 키우기",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK) return;

        _pet = NewPet();
        PoopLayer.Children.Clear();
        _poops.Clear();
        _motion.PlaceOnGround(Width * 0.5 - PetBody.Width * 0.5, Bounds());

        _store.Save(_pet.ToSave(DateTimeOffset.Now));
        RefreshFace();
        Say(CareText.EggGreeting);
    }

    /// <summary>알부터 시작하는 새 펫.</summary>
    private Pet NewPet() => Wire(new Pet(DateTimeOffset.Now, _rng));

    /// <summary>
    /// 펫이 알리는 일들을 화면에 잇는다.
    /// 새로 만들 때와 불러올 때 두 곳에서 쓰므로, 연결을 빠뜨리지 않도록 한곳에 모았다.
    /// </summary>
    private Pet Wire(Pet pet)
    {
        pet.StageChanged += stage => Dispatcher.Invoke(() =>
        {
            RefreshFace();
            Say(CareText.StageUp(stage));
        });
        pet.Evolved += tier => Dispatcher.Invoke(() => Say(CareText.Evolved(tier)));
        pet.Pooped += _ => Dispatcher.Invoke(SyncPoops);
        return pet;
    }

    /// <summary>창을 화면 전체 크기로 편다. 펫은 이 투명 판 위를 돌아다닌다.</summary>
    private void CoverPrimaryScreen()
    {
        Left = SystemParameters.WorkArea.Left;
        Top = SystemParameters.WorkArea.Top;
        Width = SystemParameters.WorkArea.Width;
        Height = SystemParameters.WorkArea.Height;
    }

    private MotionBounds Bounds() => new(0, Width, Height - GroundMargin);

    // ---------- 게임 루프 ----------

    private void LoadPet()
    {
        var data = _store.Load();
        _pet = Wire(Pet.FromSave(data));

        var away = DateTimeOffset.Now - data.LastSeenAt;
        _pet.CatchUpOffline(away, DateTimeOffset.Now);

        SyncPoops();
        RefreshFace();
        Say(CareText.Greeting(_pet.Stage, _rng));
    }

    private void StartGameLoop()
    {
        _lastTick = DateTimeOffset.Now;

        // 16ms(약 60프레임). 움직임이 끊겨 보이지 않을 만큼만 자주 돈다.
        _gameLoop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _gameLoop.Tick += (_, _) =>
        {
            var now = DateTimeOffset.Now;
            var delta = now - _lastTick;
            _lastTick = now;

            _pet.Tick(delta, now);
            _pet.AddFocusTime(_activity.DrainFocused(), now);

            // 알일 때는 돌아다니지 않는다.
            if (_pet.Stage != LifeStage.Egg)
                _motion.Update(delta, Bounds(), _pet.IsAsleep);

            ApplyMotionToScreen();
            PositionBubble();
            if (_bubbleHideAt != DateTimeOffset.MinValue && now >= _bubbleHideAt) HideBubble();

            RefreshFace();
            RefreshDebug();
            RefreshTooltip(now);
        };
        _gameLoop.Start();
    }

    private void ApplyMotionToScreen()
    {
        Canvas.SetLeft(PetBody, _motion.X);
        Canvas.SetTop(PetBody, _motion.Y);

        // 걷는 방향으로 몸을 뒤집는다. 그림이 붙으면 이게 그대로 좌우 반전이 된다.
        PetBody.RenderTransformOrigin = new Point(0.5, 0.5);
        PetBody.RenderTransform = _motion.FacingLeft
            ? new ScaleTransform(-1, 1)
            : Transform.Identity;
    }

    private void RefreshFace()
    {
        PetFace.Text = _pet.Stage switch
        {
            LifeStage.Egg => "🥚",
            LifeStage.Baby => "🐣",
            LifeStage.Child => "🐤",
            _ => "🐔",
        };

        if (_pet.IsSick) PetFace.Text = "🤒";
        else if (_pet.IsAsleep) PetFace.Text = "😴";
        else if (_pet.IsSulking) PetFace.Text = "😤";
    }

    private void RefreshDebug()
    {
        DebugText.Text =
            $"단계 {_pet.Stage}  종 {(_pet.SpeciesId is "" ? "-" : _pet.SpeciesId)}  동작 {_motion.Activity}\n" +
            $"배부름 {_pet.Stats.Hunger:F0}  행복 {_pet.Stats.Happiness:F0}  " +
            $"기력 {_pet.Stats.Energy:F0}  체력 {_pet.Stats.Health:F0}  청결 {_pet.Stats.Cleanliness:F0}\n" +
            $"집중 {_pet.FocusSeconds / 3600.0:F2}h  출근 {_pet.DistinctActiveDays}일  " +
            $"응아 {_pet.PoopsOnGround}개  자리비움 {(_activity.IsIdle ? "예" : "아니오")}\n" +
            $"알 클릭 {_pet.HatchClicks}/{Balance.HatchClicksRequired}";
    }

    /// <summary>
    /// 트레이 툴팁. 60프레임마다 갱신할 이유가 없어서 2초에 한 번만 건드린다.
    /// (NotifyIcon.Text 갱신은 값싸지 않다)
    /// </summary>
    private void RefreshTooltip(DateTimeOffset now)
    {
        if (_tray is null) return;
        if ((now - _lastTooltipUpdate).TotalSeconds < 2) return;
        _lastTooltipUpdate = now;

        _tray.SetTooltip(_pet.Stage == LifeStage.Egg
            ? "아직 알이야. 클릭해서 깨워줘!"
            : $"배부름 {_pet.Stats.Hunger:F0} · 행복 {_pet.Stats.Happiness:F0} · 응아 {_pet.PoopsOnGround}");
    }

    // ---------- 말풍선 ----------

    /// <summary>한 마디 시킨다. 글자 수에 따라 읽을 시간을 준다.</summary>
    private void Say(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        SpeechText.Text = line;
        SpeechBubble.Visibility = Visibility.Visible;

        // 짧은 말이 너무 빨리 사라지지 않게 바닥을 두고, 긴 말은 더 오래 띄운다.
        var seconds = Math.Clamp(1.8 + line.Length * 0.12, 2.5, 7.0);
        _bubbleHideAt = DateTimeOffset.Now.AddSeconds(seconds);

        PositionBubble();
    }

    private void SpeechBubble_Clicked(object sender, MouseButtonEventArgs e)
    {
        HideBubble();
        e.Handled = true;
    }

    private void HideBubble()
    {
        SpeechBubble.Visibility = Visibility.Collapsed;
        _bubbleHideAt = DateTimeOffset.MinValue;
    }

    /// <summary>말풍선을 펫 머리 위에 붙인다. 화면 밖으로 삐져나가지 않게 가둔다.</summary>
    private void PositionBubble()
    {
        if (SpeechBubble.Visibility != Visibility.Visible) return;

        SpeechBubble.UpdateLayout();
        var w = SpeechBubble.ActualWidth;
        var h = SpeechBubble.ActualHeight;

        var x = Math.Clamp(_motion.X + PetBody.Width * 0.5 - w * 0.5, 0, Math.Max(0, Width - w));
        var y = Math.Max(0, _motion.Y - h - 8);

        Canvas.SetLeft(SpeechBubble, x);
        Canvas.SetTop(SpeechBubble, y);
    }

    // ---------- 돌보기 메뉴 ----------

    /// <summary>
    /// 메뉴를 열 때마다 지금 할 수 있는 것만 켠다.
    /// 눌러 봐야 거절당하는 항목을 켜 두면 사용자가 헛수고를 한다.
    /// </summary>
    private void CareMenu_Opened(object sender, RoutedEventArgs e)
    {
        foreach (var item in CareMenu.Items.OfType<MenuItem>())
        {
            if (!TryReadAction(item, out var action)) continue;

            item.IsEnabled = _pet.Stage != LifeStage.Egg && action switch
            {
                CareAction.Medicine => _pet.IsSick,
                CareAction.Play => !_pet.IsSick,
                CareAction.Clean => _pet.PoopsOnGround > 0,
                CareAction.Sleep => !_pet.IsAsleep,
                _ => true,
            };
        }
    }

    private void CareMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || !TryReadAction(item, out var action)) return;

        var result = _pet.ApplyCare(action, DateTimeOffset.Now);
        SyncPoops();
        Say(CareText.For(action, result, _rng));
        RefreshFace();
        RefreshDebug();
    }

    private static bool TryReadAction(MenuItem item, out CareAction action)
        => Enum.TryParse(item.Tag as string, out action);

    // ---------- 응아 ----------

    /// <summary>
    /// 화면의 응아 개수를 두뇌가 알고 있는 개수에 맞춘다.
    /// 늘거나 주는 경로가 여러 개(시간 경과·메뉴 청소·응아 클릭·초기화)라서,
    /// 각 경로에서 따로 더하고 빼면 언젠가 어긋난다. 한 방향으로 맞추는 쪽이 안전하다.
    /// </summary>
    private void SyncPoops()
    {
        while (_poops.Count > _pet.PoopsOnGround)
        {
            var last = _poops[^1];
            PoopLayer.Children.Remove(last);
            _poops.RemoveAt(_poops.Count - 1);
        }

        SpawnPoops(_pet.PoopsOnGround - _poops.Count);
    }

    private void SpawnPoops(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var poop = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                Background = new SolidColorBrush(Color.FromRgb(0x8D, 0x6E, 0x4A)),
                Cursor = Cursors.Hand,
                ToolTip = "클릭해서 치우기",
            };
            poop.MouseLeftButtonDown += Poop_Clicked;

            // 펫 근처 바닥에 흩어 놓되, 펫 몸통에 가려 안 보이는 일이 없도록 최소 거리를 둔다.
            // (응아는 펫보다 뒤에 그려지므로 겹치면 그냥 사라진 것처럼 보인다)
            var side = _rng.Next(2) == 0 ? -1 : 1;
            var distance = PetBody.Width * 0.8 + _rng.Next(0, 120);
            var x = Math.Clamp(_motion.X + side * distance, 0, Math.Max(0, Width - poop.Width));
            Canvas.SetLeft(poop, x);
            Canvas.SetTop(poop, Height - GroundMargin - poop.Height);

            PoopLayer.Children.Add(poop);
            _poops.Add(poop);
        }
    }

    private void Poop_Clicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement poop) return;
        if (!_pet.CleanOnePoop()) return;

        PoopLayer.Children.Remove(poop);
        _poops.Remove(poop);
        Say(CareText.For(CareAction.Clean, CareResult.Done, _rng));
        e.Handled = true;
    }

    // ---------- 펫 입력 (누르기 / 집어 옮기기) ----------

    private void PetBody_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressedAt = e.GetPosition(Stage);
        _dragging = false;
        PetBody.CaptureMouse();
        e.Handled = true;
    }

    private void PetBody_MouseMove(object sender, MouseEventArgs e)
    {
        if (!PetBody.IsMouseCaptured) return;

        var p = e.GetPosition(Stage);
        if (!_dragging)
        {
            // 살짝 흔들린 것까지 드래그로 치면 쓰다듬기가 안 된다.
            if ((p - _pressedAt).Length < DragThreshold) return;
            _dragging = true;
            _motion.BeginDrag();
        }

        _motion.DragTo(p.X - PetBody.Width * 0.5, p.Y - PetBody.Height * 0.5, Bounds());
        ApplyMotionToScreen();
    }

    private void PetBody_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        PetBody.ReleaseMouseCapture();

        if (_dragging)
        {
            _motion.EndDrag();
            _dragging = false;
            return;
        }

        // 움직이지 않았으면 쓰다듬기(알이면 부화 클릭)다.
        var now = DateTimeOffset.Now;
        if (_pet.Stage == LifeStage.Egg)
        {
            // 부화하면 StageChanged 가 대신 말하므로 여기서는 잠자코 있는다.
            _pet.RegisterEggClick(now);
        }
        else
        {
            var result = _pet.ApplyCare(CareAction.Pet, now);
            Say(CareText.For(CareAction.Pet, result, _rng));
        }

        RefreshFace();
        RefreshDebug();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _store.Save(_pet.ToSave(DateTimeOffset.Now));
        _gameLoop?.Stop();
        _activity.Dispose();

        // 트레이 아이콘을 확실히 내린다. 안 내리면 유령 아이콘이 남는다.
        _tray?.Dispose();
    }
}
