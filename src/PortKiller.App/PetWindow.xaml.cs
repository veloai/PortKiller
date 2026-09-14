using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using PortKiller.Core;
using PortKiller.Platform;

namespace PortKiller.App;

public partial class PetWindow : Window
{
    private readonly ISaveStore _store = new JsonSaveStore();
    private readonly ActivityMonitor _activity = new();

    private Pet _pet = new();
    private DispatcherTimer? _gameLoop;
    private DateTimeOffset _lastTick = DateTimeOffset.Now;

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
        LayoutPieces();
        LoadPet();
        StartGameLoop();
    }

    /// <summary>창을 화면 전체 크기로 편다. 펫은 이 투명 판 위를 돌아다닌다.</summary>
    private void CoverPrimaryScreen()
    {
        Left = SystemParameters.WorkArea.Left;
        Top = SystemParameters.WorkArea.Top;
        Width = SystemParameters.WorkArea.Width;
        Height = SystemParameters.WorkArea.Height;
    }

    private void LayoutPieces()
    {
        // 펫은 화면 아래쪽 바닥에 선다.
        Canvas.SetLeft(PetBody, Width * 0.5 - PetBody.Width * 0.5);
        Canvas.SetTop(PetBody, Height - PetBody.Height - 8);

        // 응아는 펫에서 떨어진 자리에 둔다. 둘 사이가 통과 확인 구간이다.
        Canvas.SetLeft(Poop, Width * 0.5 + 200);
        Canvas.SetTop(Poop, Height - Poop.Height - 8);

        Canvas.SetLeft(DebugPanel, 20);
        Canvas.SetTop(DebugPanel, 20);
    }

    // ---------- 게임 루프 ----------

    private void LoadPet()
    {
        var data = _store.Load();
        _pet = Pet.FromSave(data);

        var away = DateTimeOffset.Now - data.LastSeenAt;
        _pet.CatchUpOffline(away, DateTimeOffset.Now);

        _pet.StageChanged += _ => Dispatcher.Invoke(RefreshFace);
        RefreshFace();
    }

    private void StartGameLoop()
    {
        _lastTick = DateTimeOffset.Now;
        _gameLoop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _gameLoop.Tick += (_, _) =>
        {
            var now = DateTimeOffset.Now;
            _pet.Tick(now - _lastTick, now);
            _lastTick = now;

            _pet.AddFocusTime(_activity.DrainFocused(), now);

            RefreshFace();
            RefreshDebug();
        };
        _gameLoop.Start();
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
        Poop.Visibility = _pet.Stage == LifeStage.Egg ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshDebug()
    {
        DebugText.Text =
            $"단계 {_pet.Stage}  종 {(_pet.SpeciesId is "" ? "-" : _pet.SpeciesId)}\n" +
            $"배부름 {_pet.Stats.Hunger:F0}  행복 {_pet.Stats.Happiness:F0}  " +
            $"기력 {_pet.Stats.Energy:F0}  체력 {_pet.Stats.Health:F0}\n" +
            $"집중 {_pet.FocusSeconds / 3600.0:F2}h  출근 {_pet.DistinctActiveDays}일  " +
            $"자리비움 {(_activity.IsIdle ? "예" : "아니오")}\n" +
            $"알 클릭 {_pet.HatchClicks}/{Balance.HatchClicksRequired}";
    }

    // ---------- 입력 ----------

    private void PetBody_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var now = DateTimeOffset.Now;
        if (_pet.Stage == LifeStage.Egg) _pet.RegisterEggClick(now);
        else _pet.ApplyCare(CareAction.Pet, now);

        RefreshFace();
        RefreshDebug();
    }

    private void Poop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pet.ApplyCare(CareAction.Clean, DateTimeOffset.Now);
        RefreshDebug();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _store.Save(_pet.ToSave(DateTimeOffset.Now));
        _activity.Dispose();
    }
}
