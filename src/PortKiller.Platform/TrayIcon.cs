using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace PortKiller.Platform;

/// <summary>
/// 트레이 아이콘과 우클릭 메뉴.
///
/// 화면 계층(WPF)에 대해 아무것도 모른다. 바깥과는 이벤트로만 이야기한다.
/// 창을 직접 알면 트레이가 화면 구조에 묶여서, 나중에 창을 바꿀 때 같이 부서진다.
///
/// 개인정보·보안 설계 (사내 배포 전제):
///   - 윈도우 외부 함수 선언을 하나도 늘리지 않는다. NotifyIcon 만으로 끝낸다.
///     보안 검토는 "외부 함수 호출 목록"을 보고 판단하므로, 그 목록이 짧을수록 통과가 쉽다.
///   - 입력을 읽는 함수(SetWindowsHookEx, GetAsyncKeyState, GetCursorPos)를 부르지 않는다.
///     트레이 메뉴는 윈도우가 알아서 띄워 주므로 커서 좌표를 알 필요가 없다.
///   - 통신 코드가 없다.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    /// <summary>NotifyIcon.Text 는 63자를 넘기면 예외를 던진다. 던지게 두지 않고 잘라서 넣는다.</summary>
    private const int TooltipMaxLength = 63;

    private readonly Icon _icon;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _alwaysOnTopItem;
    private readonly ToolStripMenuItem _autostartItem;
    private readonly ToolStripMenuItem _hideItem;

    private bool _disposed;

    /// <summary>아이콘을 좌클릭했을 때. 상태 창을 보여 달라는 뜻이다.</summary>
    public event Action? StatsRequested;

    /// <summary>수첩(할 일·리마인더·뽀모도로)을 열어 달라는 뜻.</summary>
    public event Action? NotebookRequested;

    /// <summary>펫을 알부터 다시 키우겠다는 뜻. 되돌릴 수 없으므로 확인은 받는 쪽에서 한다.</summary>
    public event Action? ResetRequested;

    /// <summary>항상 위 토글. 인자는 켠 뒤의 상태다.</summary>
    public event Action<bool>? AlwaysOnTopToggled;

    /// <summary>펫을 잠깐 치워 달라는 뜻. 인자가 true 면 보이는 상태다.</summary>
    public event Action<bool>? VisibilityToggled;

    public event Action? QuitRequested;

    public TrayIcon()
    {
        _icon = CreateIcon();

        _alwaysOnTopItem = new ToolStripMenuItem("항상 위")
        {
            // 바탕화면 캐릭터는 가려지면 존재 이유가 없다. 그래서 켜진 채로 시작한다.
            CheckOnClick = true,
            Checked = true,
        };
        _alwaysOnTopItem.CheckedChanged += (_, _) => AlwaysOnTopToggled?.Invoke(_alwaysOnTopItem.Checked);

        _autostartItem = new ToolStripMenuItem("시작 시 자동 실행")
        {
            CheckOnClick = true,
            // 기본값은 꺼짐이다. 레지스트리에 실제로 등록돼 있을 때만 체크가 켜진다.
            // 켜져 보이는데 실제로는 아닌 상태가 되면 사용자가 속는 셈이라, 화면이 아니라 레지스트리를 믿는다.
            Checked = ReadAutostartState(),
        };
        // 자동 실행은 바깥에 넘기지 않고 여기서 끝낸다. 창과 아무 관계가 없는 설정이다.
        _autostartItem.Click += (_, _) => ApplyAutostart(_autostartItem.Checked);

        // 잠깐 치우기. 발표·화면 공유·좁은 화면에서 캐릭터가 방해가 되는 순간이 있고,
        // 그때 종료밖에 길이 없으면 사용자는 아예 지워 버린다.
        // 체크가 켜져 있으면 "보이는 중"이다 — 메뉴 글자가 상태를 말하도록 둔다.
        _hideItem = new ToolStripMenuItem("화면에 보이기")
        {
            CheckOnClick = true,
            Checked = true,
        };
        _hideItem.CheckedChanged += (_, _) => VisibilityToggled?.Invoke(_hideItem.Checked);

        var statsItem = new ToolStripMenuItem("상태 보기");
        statsItem.Click += (_, _) => StatsRequested?.Invoke();

        var notebookItem = new ToolStripMenuItem("📔 수첩 (할 일·리마인더·집중)");
        notebookItem.Click += (_, _) => NotebookRequested?.Invoke();

        var resetItem = new ToolStripMenuItem("🥚 처음부터 다시 키우기");
        resetItem.Click += (_, _) => ResetRequested?.Invoke();

        var quitItem = new ToolStripMenuItem("종료");
        quitItem.Click += (_, _) => QuitRequested?.Invoke();

        _menu = new ContextMenuStrip();
        _menu.Items.AddRange(new ToolStripItem[]
        {
            statsItem,
            notebookItem,
            new ToolStripSeparator(),
            _hideItem,
            _alwaysOnTopItem,
            _autostartItem,
            new ToolStripSeparator(),
            resetItem,
            quitItem,
        });

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "PortKiller Pet",
            ContextMenuStrip = _menu,
            Visible = true,
        };

        // Click 은 좌·우 버튼 모두에서 올라온다. 그대로 쓰면 우클릭할 때마다 메뉴와 상태 창이 같이 뜬다.
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) StatsRequested?.Invoke();
        };
    }

    /// <summary>툴팁 문구를 바깥에서 갱신한다. 길이 제한은 여기서 감당한다.</summary>
    public void SetTooltip(string text)
    {
        if (_disposed) return;

        var safe = text ?? string.Empty;
        if (safe.Length > TooltipMaxLength) safe = safe[..TooltipMaxLength];
        _notifyIcon.Text = safe;
    }

    private static bool ReadAutostartState()
    {
        try
        {
            return AutostartManager.IsEnabled();
        }
        catch
        {
            // 레지스트리를 못 읽는 PC 라면 "등록 안 됨"으로 본다. 꺼진 쪽이 안전한 기본값이다.
            return false;
        }
    }

    private void ApplyAutostart(bool enable)
    {
        try
        {
            if (enable) AutostartManager.Enable();
            else AutostartManager.Disable();
        }
        catch
        {
            // 레지스트리 쓰기는 정책·권한 때문에 막힐 수 있다. 메뉴 핸들러에서 예외가 새면 앱이 통째로 죽는다.
            // 실패했으면 체크 표시를 실제 레지스트리 상태로 되돌려, 화면이 거짓말을 하지 않게 한다.
        }

        _autostartItem.Checked = ReadAutostartState();
    }

    /// <summary>
    /// exe 옆의 <c>assets/icons/tray.ico</c> 를 쓴다. 그 파일이 없으면 코드로 파란 원을 그린다.
    ///
    /// <para>왜 .ico 파일인가: 트레이는 화면 배율에 따라 16·20·24·32px 중 하나를 골라 간다.
    /// 한 크기만 주면 윈도우가 직접 줄이는데, 그 결과가 우리가 미리 줄여 둔 것보다 나쁘다.
    /// .ico 는 여러 크기를 한 파일에 담아서 윈도우가 <b>고르게</b> 한다.</para>
    ///
    /// <para>없을 때 대신 그리는 원을 남겨 두는 이유: 그림 파일이 빠지거나 읽기에 실패해도
    /// 트레이 아이콘은 떠야 한다. 트레이가 없으면 앱을 끌 방법이 사라진다 —
    /// 작업 표시줄에도 안 나오는 창이기 때문이다.</para>
    ///
    /// <para>GetHicon 이 만든 핸들은 Icon.Dispose 로는 안 없어지지만, DestroyIcon 을 부르려면
    /// 외부 함수 선언을 새로 늘려야 한다. 프로그램당 한 번만 만드는 핸들 하나와
    /// "외부 함수 호출 목록이 늘어나는 것" 중에서 앞을 택했다.</para>
    /// </summary>
    private static Icon CreateIcon()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "assets", "icons", "tray.ico");
        if (File.Exists(file))
        {
            try
            {
                return new Icon(file);
            }
            catch (Exception)
            {
                // 깨진 아이콘 파일 하나로 앱이 못 뜨게 두지 않는다. 아래 원으로 내려간다.
            }
        }

        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var body = new SolidBrush(Color.FromArgb(255, 96, 165, 250));
            g.FillEllipse(body, 3, 3, 26, 26);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Visible 을 먼저 내린다. Dispose 만 하면 트레이 칸이 남아서, 마우스를 올리기 전까지
        // 죽은 아이콘이 계속 보인다. 유령 아이콘의 실제 원인이 이 순서다.
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }
}
