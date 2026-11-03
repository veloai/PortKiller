using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PortKiller.Core;
using PortKiller.Platform;
using PortKiller.App.Game;
using PortKiller.Game.Input;
using PortKiller.Game.Profiles;

namespace PortKiller.App;

public partial class PetWindow : Window, IPetMcpHost
{
    /// <summary>펫이 밟고 설 바닥을 화면 아래에서 이만큼 띄운다.</summary>
    private const double GroundMargin = 8.0;

    /// <summary>이 거리 안에서 손을 떼면 "끌었다"가 아니라 "눌렀다"로 본다.</summary>
    private const double DragThreshold = 4.0;

    /// <summary>걷기 두 장을 번갈아 보여 주는 간격. 짧으면 종종걸음, 길면 느릿하다.</summary>
    private static readonly TimeSpan WalkFrame = TimeSpan.FromMilliseconds(220);

    /// <summary>먹기·기뻐하기처럼 잠깐 짓는 표정이 유지되는 시간.</summary>
    private static readonly TimeSpan ReactionHold = TimeSpan.FromSeconds(1.4);

    /// <summary>
    /// 주기 저장 간격. 원래는 <b>정상 종료 때만</b> 저장했는데, 그러면 작업 관리자로 끄거나
    /// 로그오프하거나 튕기는 순간 그날 키운 것이 통째로 사라진다. 쓰기는 임시 파일에 쓰고
    /// 이름을 바꾸는 방식이라(JsonSaveStore) 중간에 끊겨도 기존 파일이 깨지지 않는다.
    /// </summary>
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(15);

    private readonly ISaveStore _store = new JsonSaveStore(ArgValue("--save-dir"));
    private readonly ActivityMonitor _activity = new();
    private readonly Random _rng = new();
    private readonly List<FrameworkElement> _poops = new();
    private static readonly IReadOnlyList<VoxelBox> EggPlain = VoxelProps.Egg(cracked: false);
    private static readonly IReadOnlyList<VoxelBox> EggCracked = VoxelProps.Egg(cracked: true);
    private static readonly IReadOnlyList<VoxelBox> PoopBoxes = VoxelProps.Poop();

    /// <summary>알 그림. 알일 때만 보인다.</summary>
    private readonly VoxelPropView _eggView = new(28, VoxelProps.EggHeight) { Visibility = Visibility.Collapsed };

    /// <summary>바구니 속 잠든 새끼. 새끼형 종이 갓 태어났을 때만 보인다.</summary>
    private readonly VoxelPropView _cubView = new(20, VoxelProps.CubHeight) { Visibility = Visibility.Collapsed };

    /// <summary>두 발 동물(병아리)의 모델. 네발 동물은 XAML 의 <c>Quad</c> 가 맡는다.</summary>
    private readonly BipedView _biped = new() { Visibility = Visibility.Collapsed };

    /// <summary>종·상태별 알/새끼 모델 캐시. 같은 설계도 객체를 다시 주어야 화면이 새로 만들지 않는다.</summary>
    private readonly Dictionary<string, IReadOnlyList<VoxelBox>> _birthModels = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    private readonly ApiKeyStore _keys = new();
    private readonly AiSettingsStore _aiStore = new();
    private readonly NacCredentialStore _nac = new();
    private readonly CancellationTokenSource _nacCancel = new();
    private readonly CancellationTokenSource _devCancel = new();

    /// <summary>
    /// 앱 전체가 하나만 쓴다. 물어볼 때마다 새로 만들면 연결이 쌓여 소켓이 마른다.
    /// 시간 제한은 요청마다 취소 토큰으로 준다 - 설정에서 바꿀 수 있어야 하기 때문이다.
    /// </summary>
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private Pet _pet = new();
    private Notebook _notebook = new();
    private AiSettings _ai = new();
    private PetMotion _motion = null!;
    private TrayIcon? _tray;
    private NotebookWindow? _notebookWindow;
    private AskWindow? _askWindow;
    private AiSettingsWindow? _aiSettingsWindow;
    private NacSettingsWindow? _nacSettingsWindow;
    private UnifiedInputDispatcher? _gameDispatcher;
    private GameMappingWindow? _gameMappingWindow;
    private DispatcherTimer? _gameLoop;
    private DateTimeOffset _lastTick = DateTimeOffset.Now;
    private DateTimeOffset _lastTooltipUpdate = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSave = DateTimeOffset.Now;

    private Point _pressedAt;
    private bool _dragging;
    private DateTimeOffset _bubbleHideAt = DateTimeOffset.MinValue;

    /// <summary>잠깐 짓는 표정. 시간이 지나면 저절로 평소 얼굴로 돌아간다.</summary>
    private PetPose? _reaction;
    private DateTimeOffset _reactionUntil = DateTimeOffset.MinValue;

    public PetWindow()
    {
        InitializeComponent();
        BodyLayers.Children.Add(_eggView);
        BodyLayers.Children.Add(_cubView);
        BodyLayers.Children.Add(_biped);

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
        _motion.PlaceOnGround(DefaultX(), Bounds());

        // 윈도우가 로그오프·재시작·종료될 때 창 닫기 절차가 항상 오지는 않는다. 이 신호에서 먼저 저장해 둔다.
        Application.Current.SessionEnding += (_, _) => SaveNow();

        DebugPanel.CloseRequested += () => SetBriefing(false);
        DebugPanel.MouseMove += BriefingCard_MouseMove;
        DebugPanel.MouseLeave += (_, _) =>
        {
            ActiveView.PointAt(null);
            DebugPanel.ClearPointer();
        };

        _ai = _aiStore.Load();

        LoadPet();
        SetUpTray();
        EnsureKindChosen();
        StartGameLoop();
        OpenNotebookIfRequested();
        _ = ReportGenianOnStartAsync();
        _ = WatchDevStatusAsync();
        StartMcpServer();
        _ = AutoLoginNacOnStartAsync();
    }

    /// <summary>
    /// 검사용 통로. `--open-notebook` 으로 켜면 수첩을 바로 연다.
    ///
    /// 수첩은 트레이 메뉴로만 열리는데, 윈도우 11 은 새 트레이 아이콘을 숨김 영역에 넣어서
    /// 자동 검사가 그 메뉴에 닿지 못한다. 그래서 화면이 실제로 그려지는지 확인할 길을
    /// 프로젝트 안에 하나 둔다. 인자를 안 주면 아무 일도 하지 않는다.
    /// </summary>
    private void OpenNotebookIfRequested()
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Contains("--open-notebook")) OpenNotebook();
        if (args.Contains("--open-ask")) OpenAsk();
        if (args.Contains("--open-ai-settings")) OpenAiSettings();
        if (args.Contains("--briefing")) SetBriefing(true);

        // 검사용: 모든 종의 모습을 PNG 한 장으로 그려 내고 끈다(화면이 잠겨 있어도 된다).
        var sheetAt = Array.IndexOf(args, "--render-sheet");
        if (sheetAt >= 0 && sheetAt + 1 < args.Length)
        {
            ContactSheet.Render(args[sheetAt + 1]);
            Application.Current.Shutdown();
        }

        var pickerAt = Array.IndexOf(args, "--render-picker");
        if (pickerAt >= 0 && pickerAt + 1 < args.Length)
        {
            ContactSheet.RenderPicker(args[pickerAt + 1]);
            Application.Current.Shutdown();
        }
        var previewAt = Array.IndexOf(args, "--model-preview");
        if (previewAt >= 0 && previewAt + 1 < args.Length) new ModelPreviewWindow(args[previewAt + 1]).Show();
    }

    // ---------- 트레이 ----------

    /// <summary>
    /// NotifyIcon 은 메시지 펌프가 있는 스레드에서 만들어야 한다.
    /// 여기(UI 스레드)에서 만들면 이벤트도 UI 스레드로 올라와 창을 바로 만질 수 있다.
    /// </summary>
    private void SetUpTray()
    {
        _tray = new TrayIcon();
        _tray.StatsRequested += ToggleStatus;
        _tray.AlwaysOnTopToggled += on => Topmost = on;

        // 잠깐 치우기. 창만 숨기고 펫은 계속 산다 — 배고픔도 응아도 그대로 흐른다.
        // 숨긴 상태는 저장하지 않는다. 껐다 켰는데 아무것도 안 보이면 고장으로 보인다.
        _tray.VisibilityToggled += visible =>
        {
            if (visible) Show(); else Hide();
        };

        _tray.NotebookRequested += OpenNotebook;
        _tray.AskRequested += OpenAsk;
        _tray.AiSettingsRequested += OpenAiSettings;
        _tray.NacSettingsRequested += OpenNacSettings;
        _tray.ResetRequested += ConfirmAndReset;
        _tray.QuitRequested += Close;
    }

    /// <summary>
    /// 지금 설정에 맞는 AI 클라이언트. 물어볼 때마다 새로 고른다 -
    /// 설정 창에서 곳을 바꾸면 창을 다시 열지 않아도 바로 반영돼야 한다.
    /// </summary>
    private IAiClient CurrentAiClient()
    {
        // 검사용 통로. --stub-ai 로 켜면 아무 데도 보내지 않고 정해진 답을 돌려준다.
        //
        // 주소를 환경변수로 바꿔치기하는 방식은 쓰지 않았다. 그러면 배포본에
        // "아무 서버로나 보낼 수 있는 구멍"이 남아서, 보안 검토에서 바로 걸린다.
        // 이 가짜는 통신을 아예 하지 않으므로 어디로도 나갈 수 없다.
        // 인자를 안 주면 만들어지지도 않는다.
        if (Environment.GetCommandLineArgs().Contains("--stub-ai")) return new StubAiClient();

        return _ai.Provider == AiProvider.OpenAi
            ? new OpenAiChatClient(_http, () => _keys.Get(AiProvider.OpenAi), _ai)
            : new GeminiChatClient(_http, () => _keys.Get(AiProvider.Gemini), _ai);
    }

    /// <summary>검사에서만 쓰는 가짜. 네트워크를 만지지 않는다.</summary>
    private sealed class StubAiClient : IAiClient
    {
        public AiProvider Provider => AiProvider.Gemini;

        public Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default)
            => Task.FromResult($"STUB ANSWER 42 (물어본 말: {prompt})");
    }

    /// <summary>
    /// 물어보기 창. 하나만 띄운다.
    ///
    /// 답이 오면 펫이 짧게 한마디 한다 - 창을 다른 창 뒤로 보내 놔도 "답이 왔다"를 알 수 있게.
    /// 답 전체를 말풍선에 넣지는 않는다. 화면을 덮어 버린다.
    /// </summary>
    private void OpenAsk()
    {
        if (_askWindow is not null)
        {
            _askWindow.Activate();
            return;
        }

        _askWindow = new AskWindow(CurrentAiClient, _ai, () => _keys.Has(_ai.Provider));
        _askWindow.Answered += _ =>
        {
            Say(CareText.AiAnswered(_rng));
            React(PetPose.Happy);
        };
        _askWindow.Closed += (_, _) => _askWindow = null;
        _askWindow.Show();
    }

    private void OpenAiSettings()
    {
        if (_aiSettingsWindow is not null)
        {
            _aiSettingsWindow.Activate();
            return;
        }

        _aiSettingsWindow = new AiSettingsWindow(_ai, _keys, _aiStore);
        _aiSettingsWindow.Saved += () => _askWindow?.RefreshProviderLabel();
        _aiSettingsWindow.Closed += (_, _) => _aiSettingsWindow = null;
        _aiSettingsWindow.Show();
    }

    private void OpenNacSettings()
    {
        if (_nacSettingsWindow is not null)
        {
            _nacSettingsWindow.Activate();
            return;
        }

        _nacSettingsWindow = new NacSettingsWindow(_nac);
        _nacSettingsWindow.TryNowRequested += async () =>
        {
            var result = await TryNacLoginAsync(TimeSpan.FromSeconds(3));
            SayNacResult(result);
            return result;
        };
        _nacSettingsWindow.Closed += (_, _) => _nacSettingsWindow = null;
        _nacSettingsWindow.Show();
    }

    // ---------- 개발 환경 상태 ----------

    /// <summary>처음 확인까지 기다리는 시간. 로그인 직후엔 Docker 가 아직 뜨는 중이라 바로 보면 다 멈춤으로 나온다.</summary>
    private static readonly TimeSpan DevFirstDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DevInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Docker·컨테이너·로컬 서버가 돌고 있는지 1분마다 본다(<see cref="DevStatusProbe"/>).
    /// 결과는 브리핑 카드의 "개발 환경" 칸에 그리고, 멈추거나 되살아나면 펫이 말한다.
    /// </summary>
    private async Task WatchDevStatusAsync()
    {
        var specs = await Task.Run(DevStatusProbe.LoadSpecs);
        IReadOnlyList<DevCheck>? last = null;

        try
        {
            await Task.Delay(DevFirstDelay, _devCancel.Token);
            while (!_devCancel.IsCancellationRequested)
            {
                var now = await Task.Run(() => DevStatusProbe.Run(specs));
                var changes = DevStatusRules.Diff(last, now);
                DebugPanel.UpdateDev(now);

                foreach (var change in changes)
                {
                    Say(DevStatusRules.Line(change));
                    React(change.To == DevState.Down ? PetPose.Sulk : PetPose.Happy);
                }

                last = now;
                _lastDev = now;
                await Task.Delay(DevInterval, _devCancel.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // ---------- MCP 서버 ----------

    private McpHttpHost? _mcp;
    private IReadOnlyList<DevCheck>? _lastDev;

    /// <summary>
    /// Claude Code·ALFA 같은 에이전트가 펫을 부를 수 있게 MCP 서버를 연다(127.0.0.1:47420, 열쇠 필요).
    /// 포트가 이미 쓰이고 있거나 실패하면 MCP 없이 계속 돈다. 결과는 startup.log 에 남긴다.
    /// </summary>
    private void StartMcpServer()
    {
        // 검사용 실행(--save-dir 등)은 진짜 펫의 포트를 빼앗지 않게 열지 않는다.
        if (Environment.GetCommandLineArgs().Skip(1).Any(a => a.StartsWith("--", StringComparison.Ordinal)
                                                              && a != "--briefing")) return;
        try
        {
            var router = new McpRouter(PetMcpTools.Build(this));
            _mcp = new McpHttpHost(router, McpHttpHost.LoadOrCreateKey());
            StartupLog.Write(_mcp.Start() ? $"mcp listening 127.0.0.1:{_mcp.Port}" : "mcp port busy - not started");
        }
        catch (Exception ex)
        {
            StartupLog.Crash("mcp", ex);
        }
    }

    // MCP 요청은 백그라운드 스레드에서 온다. 화면은 UI 스레드에서만 만진다.

    void IPetMcpHost.Say(string text, PetPose? reaction) => Dispatcher.Invoke(() =>
    {
        Say(text);
        if (reaction is { } pose) React(pose);
    });

    void IPetMcpHost.Briefing(bool show) => Dispatcher.Invoke(() => SetBriefing(show));

    System.Text.Json.Nodes.JsonObject IPetMcpHost.Status() => Dispatcher.Invoke(() =>
    {
        var s = _pet.Stats;
        return new System.Text.Json.Nodes.JsonObject
        {
            ["kind"] = _pet.KindInfo.Id,
            ["kindName"] = _pet.KindInfo.Name,
            ["stage"] = _pet.Stage.ToString(),
            ["variant"] = _pet.SpeciesId,
            ["stats"] = new System.Text.Json.Nodes.JsonObject
            {
                ["hunger"] = Math.Round(s.Hunger),
                ["happiness"] = Math.Round(s.Happiness),
                ["energy"] = Math.Round(s.Energy),
                ["health"] = Math.Round(s.Health),
                ["cleanliness"] = Math.Round(s.Cleanliness),
            },
            ["sick"] = _pet.IsSick,
            ["sulking"] = _pet.IsSulking,
            ["asleep"] = _pet.IsAsleep,
            ["poopsOnGround"] = _pet.PoopsOnGround,
            ["focusHours"] = Math.Round(_pet.FocusSeconds / 3600.0, 1),
            ["summary"] = Briefing.Summary(s),
        };
    });

    System.Text.Json.Nodes.JsonObject IPetMcpHost.Dev() => Dispatcher.Invoke(() =>
    {
        var checks = _lastDev;
        var list = new System.Text.Json.Nodes.JsonArray();
        foreach (var c in checks ?? Array.Empty<DevCheck>())
        {
            list.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["name"] = c.Name,
                ["state"] = c.State.ToString().ToLowerInvariant(),
                ["detail"] = c.Detail,
            });
        }

        return new System.Text.Json.Nodes.JsonObject
        {
            ["checked"] = checks is not null,
            ["summary"] = checks is null ? "아직 확인 전이에요(펫이 켜지고 20초 뒤 첫 확인)." : DevStatusRules.Summary(checks),
            ["checks"] = list,
        };
    });

    /// <summary>
    /// 켤 때마다 Genian 이 구동 중인지 확인해 true/false 로 알린다. 자동 로그인이 켜져 있든 아니든 한다.
    /// 말풍선은 금방 사라지므로 같은 내용을 <c>%APPDATA%\PortKillerPet\genian-status.txt</c> 에도 남긴다
    /// (재부팅 뒤 "자동으로 떴는지, 그때 Genian 은 어땠는지"를 나중에 볼 수 있게).
    /// </summary>
    private async Task ReportGenianOnStartAsync()
    {
        var status = await Task.Run(NacAutoLogin.Probe);
        var cred = _nac.Load();
        var autostart = false;
        try { autostart = AutostartManager.IsEnabled(); } catch (Exception) { /* 못 읽으면 꺼진 것으로 */ }

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} genian={(status.Running ? "true" : "false")} " +
                   $"processes=[{string.Join(",", status.Processes)}] loginWindow={(status.LoginWindow ? "true" : "false")} " +
                   $"nacAutoLogin={(cred is { Enabled: true } ? "on" : cred is null ? "no-credential" : "off")} " +
                   $"autostart={(autostart ? "true" : "false")}";
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PortKillerPet");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "genian-status.txt"), line + Environment.NewLine);
        }
        catch (Exception)
        {
            // 기록을 못 남겨도 화면 알림은 한다.
        }

        // 켜질 때 하는 다른 인사말에 덮이지 않게 잠깐 뒤에 말한다.
        await Task.Delay(2500);
        Say($"Genian 실행 중: {(status.Running ? "true" : "false")}" +
            (status.LoginWindow ? " (로그인 창 보임)" : string.Empty));
    }

    /// <summary>
    /// 펫이 켜질 때(= 재부팅 뒤 자동 실행 포함) 한 번, Genian 로그인 창을 기다렸다가 채운다.
    /// 등록·켜짐이 아니면 아무것도 하지 않는다. 기다리는 시간은 2분으로 끝난다.
    /// </summary>
    private async Task AutoLoginNacOnStartAsync()
    {
        var cred = _nac.Load();
        if (cred is not { Enabled: true }) return;

        var result = await TryNacLoginAsync(TimeSpan.FromMinutes(2));
        // 창이 안 뜬 것(이미 인증됨)은 정상이라 조용히 넘어간다. 누른 것과 실패만 알린다.
        if (result.Outcome != NacLoginOutcome.NotFound) SayNacResult(result);
    }

    private Task<NacLoginResult> TryNacLoginAsync(TimeSpan wait)
    {
        var cred = _nac.Load();
        if (cred is null)
            return Task.FromResult(new NacLoginResult(NacLoginOutcome.Failed, "먼저 아이디와 비밀번호를 저장해 주세요."));

        // UI 자동화 호출은 블로킹이라 UI 스레드에서 부르면 펫이 멈춘다.
        return Task.Run(() => NacAutoLogin.Run(cred, wait, _nacCancel.Token));
    }

    private void SayNacResult(NacLoginResult result)
    {
        Say(result.Outcome == NacLoginOutcome.Submitted
            ? "NAC 인증 눌렀어!"
            : "NAC: " + result.Message);
    }

    /// <summary>
    /// 수첩 창은 하나만 띄운다. 트레이를 여러 번 눌러 창이 겹겹이 쌓이면
    /// 어느 것이 진짜인지 알 수 없게 된다.
    /// </summary>
    private void OpenNotebook()
    {
        if (_notebookWindow is not null)
        {
            _notebookWindow.Activate();
            return;
        }

        _notebookWindow = new NotebookWindow(_notebook) { Owner = null };
        _notebookWindow.Closed += (_, _) =>
        {
            _notebookWindow = null;
            _store.Save(BuildSave());
        };
        _notebookWindow.Show();
    }

    /// <summary>새 펫 맞이하기: 확인을 받고, 종을 고르게 한 뒤, 갓 태어난 단계부터 다시 시작한다.</summary>
    private void ConfirmAndReset()
    {
        var answer = MessageBox.Show(
            "정말 새 펫을 맞이할까요?\n지금 펫과는 영영 헤어지게 돼요…",
            "새 펫 맞이하기",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK) return;

        var kind = PickKind(firstTime: false);
        if (kind is null) return;   // 고르지 않고 닫았으면 아무것도 바꾸지 않는다
        StartNewPet(kind);
    }

    /// <summary>종 고르는 창을 띄우고 고른 종 id 를 돌려준다. 고르지 않고 닫으면 null.</summary>
    private string? PickKind(bool firstTime)
    {
        var dialog = new SpeciesPickerWindow(firstTime);
        return dialog.ShowDialog() == true ? dialog.Chosen : null;
    }

    /// <summary>
    /// 저장에 종이 없으면(새 설치) 고르게 한다. 고르지 않고 닫아도 앱이 멈추지 않게 레서판다로 시작한다.
    /// 옛 저장(종이 생기기 전)은 불러올 때 이미 레서판다가 되어 있어서 여기에 걸리지 않는다.
    /// </summary>
    private void EnsureKindChosen()
    {
        if (PetKinds.IsChosen(_pet.Kind)) return;
        if (Environment.GetCommandLineArgs().Any(a => a.StartsWith("--render-"))) return;

        StartNewPet(PickKind(firstTime: true) ?? PetKinds.RedPanda);
    }

    private void StartNewPet(string kind)
    {
        SetBriefing(false);
        _pet = NewPet(kind);
        PoopLayer.Children.Clear();
        _poops.Clear();
        _motion.PlaceOnGround(DefaultX(), Bounds());

        SaveNow();
        RefreshFace();
        Say(CareText.NewbornGreeting(_pet.KindInfo));
    }

    /// <summary>갓 태어난 단계부터 시작하는 새 펫. 종은 고른 것을 쓴다.</summary>
    private Pet NewPet(string kind = PetKinds.RedPanda) => Wire(new Pet(DateTimeOffset.Now, _rng, kind));

    /// <summary>
    /// 펫이 알리는 일들을 화면에 잇는다.
    /// 새로 만들 때와 불러올 때 두 곳에서 쓰므로, 연결을 빠뜨리지 않도록 한곳에 모았다.
    /// </summary>
    private Pet Wire(Pet pet)
    {
        pet.StageChanged += stage => Dispatcher.Invoke(() =>
        {
            RefreshFace();
            Say(CareText.StageUp(stage, pet.KindInfo));
        });
        pet.Evolved += tier => Dispatcher.Invoke(() => Say(CareText.Evolved(tier)));
        pet.Pooped += _ => Dispatcher.Invoke(SyncPoops);
        return pet;
    }

    /// <summary>수첩이 알리는 일을 펫이 말하게 잇는다.</summary>
    private Notebook WireNotebook(Notebook book)
    {
        book.ReminderDue += r => Dispatcher.Invoke(() => Say(CareText.Reminder(r.Text)));

        book.AllTodosDone += () => Dispatcher.Invoke(() =>
        {
            Say(CareText.TodoDone(Notebook.TodoSlots, Notebook.TodoSlots));
            // 다 끝냈으면 같이 기뻐한다. 행복은 돌보기와 같은 경로로 올린다.
            _pet.ApplyCare(CareAction.Pet, DateTimeOffset.Now);
        });

        book.PhaseChanged += phase => Dispatcher.Invoke(() =>
        {
            // 집중이 끝나 쉬는 구간으로 넘어갈 때와 시작할 때만 말한다.
            // 그만두기로 끈 경우까지 말하면 잔소리가 된다.
            if (phase == PomodoroPhase.Idle) return;
            Say(CareText.Pomodoro(phase));
        });

        return book;
    }

    private SaveData BuildSave()
    {
        var data = _pet.ToSave(DateTimeOffset.Now);
        data.Notebook = _notebook.ToSave();
        data.LastX = _motion?.X;
        return data;
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

    /// <summary>
    /// 한 번도 안 옮겼을 때 설 자리. <b>화면 한가운데가 아니라 오른쪽 끝 가까이</b>다.
    ///
    /// <para>가운데에 세우면 처음 켜자마자 보던 것을 가린다. 바탕화면 캐릭터는 눈에 띄어야 하지만
    /// 일을 막으면 바로 지워진다. 오른쪽 아래는 트레이·알림이 사는 자리라 무언가 떠 있어도
    /// 덜 거슬린다.</para>
    /// </summary>
    private double DefaultX() => Math.Max(0, Width - PetBody.Width - 160);

    // ---------- 게임 루프 ----------

    /// <summary>
    /// 검사용 통로. `--save-dir 폴더` 를 주면 세이브를 그 폴더에서 읽고 쓴다.
    /// 진짜 펫의 저장 파일을 건드리지 않고 알·응아 같은 상태를 눈으로 보려는 것이다. 안 주면 원래 자리.
    /// </summary>
    private static string? ArgValue(string name)
    {
        var args = Environment.GetCommandLineArgs();
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private void LoadPet()
    {
        var data = _store.Load();
        _pet = Wire(Pet.FromSave(data));
        _notebook = WireNotebook(Notebook.FromSave(data.Notebook));

        // 지난번에 세워 둔 자리로 돌려놓는다. 화면이 좁아졌거나 모니터가 바뀌었을 수 있으니
        // 지금 창 안으로 가둔다 — 안 그러면 화면 밖에 서서 영영 안 보인다.
        if (data.LastX is { } savedX)
        {
            _motion.PlaceOnGround(Math.Clamp(savedX, 0, Math.Max(0, Width - PetBody.Width)), Bounds());
        }

        var away = DateTimeOffset.Now - data.LastSeenAt;
        _pet.CatchUpOffline(away, DateTimeOffset.Now);

        SyncPoops();
        RefreshFace();
        Say(CareText.Greeting(_pet.Stage, _rng, _pet.KindInfo));
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
            _notebook.Tick(now);

            // 알일 때는 돌아다니지 않는다.
            // 브리핑 중에는 땅에 서 있는 동안 가만히 있는다(집어 올리거나 떨어지는 중에는 평소대로).
            var holdStill = _briefing && _motion.Activity is not (PetActivity.Jump or PetActivity.Fall or PetActivity.Dragged);
            if (_pet.Stage != LifeStage.Egg && !holdStill)
                _motion.Update(delta, Bounds(), _pet.IsAsleep);

            ApplyMotionToScreen();
            if (_briefing) PlaceBriefingCard();
            PositionBubble();
            if (_bubbleHideAt != DateTimeOffset.MinValue && now >= _bubbleHideAt) HideBubble();

            RefreshFace();
            RefreshDebug();
            RefreshTooltip(now);
            SaveIfDue(now);
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

    /// <summary>지금 종의 몸 틀에 맞는 화면 부품(네발 / 두 발).</summary>
    private AnimalViewBase ActiveView => _pet.KindInfo.Rig == RigKind.Biped ? _biped : Quad;

    /// <summary>
    /// 지금 화면에 보여야 할 모습으로 맞춘다.
    /// 갓 태어난 단계는 태어나는 방식대로(알형은 알, 새끼형은 바구니 속 새끼), 그다음은 종의 몸 틀대로 그린다.
    /// 모두 코드로 조립한 모델이라 그림 파일이 필요 없다.
    /// 게임 루프에서 초당 60번 불리므로 모양(상자)은 바뀔 때만 다시 만든다.
    /// </summary>
    private void RefreshFace()
    {
        ClearBodyCircle();
        var kind = _pet.KindInfo;

        if (_pet.Stage == LifeStage.Egg)
        {
            Quad.Visibility = Visibility.Collapsed;
            _biped.Visibility = Visibility.Collapsed;
            var nearly = _pet.HatchClicks >= Balance.HatchClicksRequired - 5;

            if (kind.Birth == BirthKind.Egg)
            {
                _cubView.Visibility = Visibility.Collapsed;
                _eggView.Visibility = Visibility.Visible;
                _eggView.Show(BirthModel($"egg/{kind.Id}/{nearly}", () => VoxelProps.Egg(nearly, VoxelAnimals.EggSpot(kind.Id))));
                _eggView.Tilt = NewbornTilt(14);
            }
            else
            {
                _eggView.Visibility = Visibility.Collapsed;
                _cubView.Visibility = Visibility.Visible;
                _cubView.Show(BirthModel($"cub/{kind.Id}/{nearly}", () => VoxelProps.Cub(kind.Id, nearly)));
                _cubView.Tilt = NewbornTilt(7);
            }

            return;
        }

        _eggView.Visibility = Visibility.Collapsed;
        _cubView.Visibility = Visibility.Collapsed;

        var view = ActiveView;
        (view == Quad ? (UIElement)_biped : Quad).Visibility = Visibility.Collapsed;

        var pose = CurrentPose();
        view.Configure(kind.Id, _pet.SpeciesId, _pet.Stage);
        view.Animate(pose, pose is PetPose.Walk1 or PetPose.Walk2, _clock.Elapsed.TotalSeconds);
        view.Visibility = Visibility.Visible;
    }

    private IReadOnlyList<VoxelBox> BirthModel(string key, Func<IReadOnlyList<VoxelBox>> make)
    {
        if (!_birthModels.TryGetValue(key, out var model)) _birthModels[key] = model = make();
        return model;
    }

    /// <summary>그림·3D 가 있으면 초록 동그라미는 치운다. 배경을 null 이 아니라 Transparent 로 두는 이유는 위 주석 참고.</summary>
    private void ClearBodyCircle()
    {
        if (PetBody.BorderThickness.Left == 0) return;
        PetBody.Background = Brushes.Transparent;
        PetBody.BorderThickness = new Thickness(0);
    }

    /// <summary>
    /// 알·바구니가 기우는 각도. 클릭 직후에는 좌우로 크게 흔들리고(<paramref name="swing"/> 도), 평소에는 아주 조금 까딱인다.
    /// 흔들림 방향은 <see cref="Pet.HatchClicks"/> 의 홀짝으로 가른다 - 따로 타이머를 두면
    /// 클릭과 어긋나 "눌렀는데 반응이 없다"가 된다.
    /// </summary>
    private double NewbornTilt(double swing)
    {
        if (_reactionUntil > DateTimeOffset.Now) return _pet.HatchClicks % 2 == 0 ? swing : -swing;
        return Math.Sin(_clock.Elapsed.TotalSeconds * 1.6) * (swing / 7);
    }

    /// <summary>
    /// 상태가 겹칠 때 무엇을 보여 줄지 정한다. 위에 있는 것이 이긴다.
    /// 아픈 것과 삐친 것이 동시에 참일 수 있는데, 그때는 아픈 쪽이 더 급한 소식이다.
    /// </summary>
    private PetPose CurrentPose()
    {
        var now = DateTimeOffset.Now;

        if (_pet.IsSick) return PetPose.Sick;
        if (_pet.IsAsleep) return PetPose.Sleep;
        if (_reaction is { } react && now < _reactionUntil) return react;
        if (_pet.IsSulking) return PetPose.Sulk;

        return _motion.Activity switch
        {
            // 뛰어오르거나 떨어지는 중에는 들뜬 포즈를 쓴다. 그림을 따로 뽑지 않으려고
            // happy 를 겸용하기로 한 것이다(docs/assets/image-brief.md 3절).
            PetActivity.Jump or PetActivity.Fall or PetActivity.Dragged => PetPose.Happy,
            PetActivity.Walk => (now.Ticks / WalkFrame.Ticks) % 2 == 0 ? PetPose.Walk1 : PetPose.Walk2,
            _ => PetPose.Idle,
        };
    }

    /// <summary>잠깐 짓는 표정을 걸어 둔다. 시간이 지나면 <see cref="CurrentPose"/> 가 알아서 뗀다.</summary>
    private void React(PetPose pose)
    {
        _reaction = pose;
        _reactionUntil = DateTimeOffset.Now + ReactionHold;
    }

    private void RefreshDebug() => DebugPanel.Update(_pet, _activity.IsIdle);

    /// <summary>
    /// 트레이 툴팁. 60프레임마다 갱신할 이유가 없어서 2초에 한 번만 건드린다.
    /// (NotifyIcon.Text 갱신은 값싸지 않다)
    /// </summary>
    /// <summary>
    /// 가끔 저장한다. 60프레임마다 디스크를 쓸 이유는 없고, 반대로 종료 때만 쓰면
    /// 비정상 종료에 전부 잃는다. 실패해도 화면은 계속 돌아간다 —
    /// 저장 한 번 실패로 펫이 죽으면 그게 더 큰 손해다.
    /// </summary>
    /// <summary>
    /// 지금 바로 저장한다. 똥을 치우거나 밥을 주는 것처럼 사용자가 한 행동은 이 함수로 즉시 남긴다.
    /// 주기 저장에만 맡기면, 치우고 곧바로 강제 종료·재부팅될 때 그 행동이 통째로 사라져서
    /// 다음에 켜면 똥이 그대로 나온다(실제로 그랬다).
    /// </summary>
    private void SaveNow()
    {
        try
        {
            _store.Save(BuildSave());
            _lastSave = DateTimeOffset.Now;
        }
        catch (Exception)
        {
            // 디스크 문제로 실패해도 화면은 계속 돈다. 다음 주기에 다시 해 본다.
        }
    }

    private void SaveIfDue(DateTimeOffset now)
    {
        if (now - _lastSave < SaveInterval) return;
        _lastSave = now;

        try
        {
            _store.Save(BuildSave());
        }
        catch (Exception)
        {
            // 디스크가 꽉 찼거나 정책이 막았을 수 있다. 다음 차례에 또 해 본다.
        }
    }

    private void RefreshTooltip(DateTimeOffset now)
    {
        if (_tray is null) return;
        if ((now - _lastTooltipUpdate).TotalSeconds < 2) return;
        _lastTooltipUpdate = now;

        _tray.SetTooltip(_pet.Stage == LifeStage.Egg
            ? CareText.NewbornHint(_pet.KindInfo)
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

    private void AskMenu_Click(object sender, RoutedEventArgs e) => OpenAsk();

    private void NacMenu_Click(object sender, RoutedEventArgs e) => OpenNacSettings();

    private void NewPetMenu_Click(object sender, RoutedEventArgs e) => ConfirmAndReset();

    private void StatusMenu_Click(object sender, RoutedEventArgs e) => ToggleStatus();

    private void QuitMenu_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleStatus() => SetBriefing(!_briefing);

    // ---------- 브리핑(상태 보기) ----------

    private bool _briefing;

    /// <summary>
    /// 레서판다가 안경을 쓰고 서서, 옆에 세운 대시보드를 지시봉으로 짚으며 상태를 브리핑한다.
    /// 별도 창이 아니라 펫 곁에 붙어 있어서, 펫을 옮기면 카드도 따라온다.
    /// </summary>
    private void SetBriefing(bool on)
    {
        if (on == _briefing) return;
        _briefing = on;
        ActiveView.SetBriefing(on);

        if (!on)
        {
            ActiveView.PointAt(null);
            DebugPanel.Visibility = Visibility.Collapsed;
            return;
        }

        // 자리가 더 넓은 쪽(펫이 화면 오른쪽에 있으면 왼쪽)을 보고 선다.
        _motion.Face(_motion.X > Width / 2);
        RefreshDebug();
        DebugPanel.Visibility = Visibility.Visible;
        DebugPanel.Opacity = 0;
        DebugPanel.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)));
        PlaceBriefingCard();
    }

    /// <summary>카드를 펫이 보는 쪽 옆에 세운다. 화면 밖으로 나가면 반대편을 보게 한다.</summary>
    private void PlaceBriefingCard()
    {
        DebugPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = DebugPanel.DesiredSize;

        double CardX(bool left) => left ? _motion.X - size.Width + 14 : _motion.X + PetBody.Width - 14;

        var x = CardX(_motion.FacingLeft);
        if (x < 0 || x + size.Width > Width)
        {
            _motion.Face(!_motion.FacingLeft);
            x = CardX(_motion.FacingLeft);
        }

        Canvas.SetLeft(DebugPanel, Math.Clamp(x, 0, Math.Max(0, Width - size.Width)));
        Canvas.SetTop(DebugPanel, Math.Max(0, _motion.Y + PetBody.Height - size.Height));
    }

    /// <summary>지시봉이 가장 위(카드 맨 위)를 가리킬 때와 가장 아래를 가리킬 때의 각도(도). 몸이 보는 쪽이 0°다.</summary>
    private const double PointTopAngle = 78, PointBottomAngle = -32;

    /// <summary>
    /// 마우스가 카드 위를 지나갈 때만 온다(내 창 안의 이벤트). 카드 안에서 마우스가 있는 높이에 맞춰
    /// 지시봉을 크게 들었다 내린다 - 맨 위면 하늘을 찌르고 맨 아래면 바닥을 가리킨다.
    /// 화면 전체 커서 위치를 읽는 것이 아니다. 카드 밖에서는 이 함수가 불리지 않는다.
    ///
    /// <para>어깨에서 마우스까지의 실제 각도를 쓰지 않은 이유: 카드가 멀리 서 있어서 그 각도는 좁은 범위
    /// (10~40°)에서만 움직여, 지시봉이 거의 안 움직이는 것처럼 보였다. 카드 안의 높이를 각도로 옮기면
    /// 마우스를 조금만 움직여도 지시봉이 크게 따라온다.</para>
    /// </summary>
    private void BriefingCard_MouseMove(object sender, MouseEventArgs e)
    {
        var onCard = e.GetPosition(DebugPanel);
        var t = DebugPanel.ActualHeight > 0 ? Math.Clamp(onCard.Y / DebugPanel.ActualHeight, 0, 1) : 0.5;

        ActiveView.PointAt(PointTopAngle + (PointBottomAngle - PointTopAngle) * t);
        DebugPanel.Pointer(onCard);
    }

    private void GameMenu_Click(object sender, RoutedEventArgs e) => OpenGameSettings();

    private void OpenGameSettings()
    {
        if (_gameMappingWindow is not null)
        {
            _gameMappingWindow.Activate();
            return;
        }

        _gameDispatcher ??= new UnifiedInputDispatcher(DefaultProfiles.CreateWoWClassicProfile());
        _gameDispatcher.VoiceCommandExecuted += (cmd, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                Say($"⚔️ {cmd.Phrase}!");
                React(PetPose.Happy);
            });
        };

        _gameMappingWindow = new GameMappingWindow(_gameDispatcher);
        _gameMappingWindow.Closed += (_, _) => _gameMappingWindow = null;
        _gameMappingWindow.Show();
    }

    private void CareMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || !TryReadAction(item, out var action)) return;

        var result = _pet.ApplyCare(action, DateTimeOffset.Now);

        // 받아들여졌을 때만 표정을 바꾼다. 거절(배가 불러 못 먹음 등)에도 좋아하면 거짓말이 된다.
        if (result == CareResult.Done)
        {
            switch (action)
            {
                case CareAction.Feed or CareAction.Snack: React(PetPose.Eat); break;
                case CareAction.Play or CareAction.Pet: React(PetPose.Happy); break;
            }
        }

        SyncPoops();
        SaveNow();
        Say(CareText.For(action, result, _rng, _pet.KindInfo));
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
            // 눌림은 투명한 판(Border)이 받는다. 3D 그림은 눌림을 받지 않는다.
            var view = new VoxelPropView(10, VoxelProps.PoopHeight);
            view.Show(PoopBoxes);
            var poop = new Border
            {
                Width = 40,
                Height = 40,
                Background = Brushes.Transparent,
                Child = view,
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
        SaveNow();
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
            React(PetPose.Idle);   // 알은 포즈 대신 "방금 눌렸다"는 사실만 쓴다(EggPose 참고)
        }
        else
        {
            var result = _pet.ApplyCare(CareAction.Pet, now);
            if (result == CareResult.Done) React(PetPose.Happy);
            Say(CareText.For(CareAction.Pet, result, _rng, _pet.KindInfo));
        }

        RefreshFace();
        RefreshDebug();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _store.Save(BuildSave());
        _notebookWindow?.Close();
        _nacCancel.Cancel();
        _devCancel.Cancel();
        _mcp?.Dispose();
        _gameMappingWindow?.Close();
        _gameDispatcher?.Dispose();
        _gameLoop?.Stop();
        _activity.Dispose();

        // 트레이 아이콘을 확실히 내린다. 안 내리면 유령 아이콘이 남는다.
        _tray?.Dispose();
    }
}
