using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace PortKiller.Platform;

public enum NacLoginOutcome
{
    /// <summary>시간 안에 로그인 창이 나타나지 않았다(이미 인증된 상태일 수 있다).</summary>
    NotFound,

    /// <summary>아이디·비밀번호를 채우고 인증 버튼을 눌렀다.</summary>
    Submitted,

    /// <summary>창은 찾았지만 채우지 못했다. 사유는 메시지에 있다.</summary>
    Failed,
}

public sealed record NacLoginResult(NacLoginOutcome Outcome, string Message);

/// <summary>지금 Genian 이 어떤 상태인지 한 번 들여다본 결과.</summary>
/// <param name="Running">Genian 에이전트(GnAgent)가 실행 중인가.</param>
/// <param name="Processes">실행 중인 Genian 계열(Gn…) 프로세스 이름들.</param>
/// <param name="LoginWindow">로그인 창(아이디·비밀번호 칸과 [인증] 버튼)이 지금 떠 있는가.</param>
public sealed record GenianStatus(bool Running, IReadOnlyList<string> Processes, bool LoginWindow);

/// <summary>
/// Genian NAC 로그인 창에 아이디·비밀번호를 채우고 [인증]을 누른다.
///
/// <para><b>입력 방식</b>: 윈도우 UI 자동화(<c>System.Windows.Automation</c>)의 값 설정·누르기다.
/// 키 입력을 흉내 내거나(SendInput) 창 메시지를 보내는 방식이 아니라서 외부 함수 선언이 늘지 않는다.
/// 관리자 권한 창에는 닿지 못한다(윈도우가 막는다) — 그 경우 실패로 알리고 끝낸다. 우회하지 않는다.</para>
///
/// <para><b>엉뚱한 창에 비밀번호를 넣지 않는다</b>: 제목이 아니라 <b>창을 만든 프로세스 이름</b>이
/// <c>Gn</c> 으로 시작하는(Genian 에이전트) 창만 대상으로 한다. 거기에 더해 비밀번호 칸과
/// [인증] 버튼이 함께 있어야 로그인 창으로 본다. 다른 프로그램 창은 프로세스 이름만 보고 지나가며
/// 제목·내용은 읽지 않는다.</para>
///
/// <para><b>한 번만 시도한다</b>: 비밀번호가 틀렸을 때 계속 두드리면 계정이 잠긴다.
/// 눌렀으면 끝이고, 다시 하려면 사용자가 직접 요청해야 한다.</para>
/// </summary>
public static class NacAutoLogin
{
    /// <summary>대상 프로세스 이름의 앞부분. Genian 에이전트는 GnAgent·GnStart·GnPlugin 처럼 Gn 으로 시작한다.</summary>
    private const string ProcessPrefix = "Gn";

    private const string SubmitButtonName = "인증";

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1.5);

    /// <summary>창이 나타날 때까지 기다린다(최대 <paramref name="wait"/>). 블로킹 호출이므로 백그라운드에서 부른다.</summary>
    public static NacLoginResult Run(NacCredential cred, TimeSpan wait, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + wait;
        while (!ct.IsCancellationRequested)
        {
            foreach (var window in GenianWindows())
            {
                var result = TryFill(window, cred);
                if (result is not null) return Logged(result);
            }

            if (DateTime.UtcNow >= deadline) break;
            if (ct.WaitHandle.WaitOne(PollInterval)) break;
        }

        // Genian 창은 있는데 로그인 창으로 알아보지 못한 경우를 위해 구조를 남겨 둔다(원인을 다음에 볼 수 있게).
        if (GenianWindows().Count > 0) TryWriteDump();
        return Logged(new NacLoginResult(NacLoginOutcome.NotFound, "로그인 창이 보이지 않았어요."));
    }

    /// <summary>
    /// Genian 이 구동 중인지 본다. 프로세스 <b>이름</b>만 보고(Gn 으로 시작하는 것), 창은 로그인 창이 있는지만 본다.
    /// 아무것도 입력하지 않는다. "자동 로그인이 제대로 붙을 자리에 Genian 이 있는가"를 확인하는 용도다.
    /// </summary>
    public static GenianStatus Probe()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.ProcessName.StartsWith(ProcessPrefix, StringComparison.OrdinalIgnoreCase))
                    names.Add(p.ProcessName);
            }
        }

        var loginWindow = false;
        if (names.Count > 0)
        {
            foreach (var window in GenianWindows())
            {
                if (LooksLikeLoginWindow(window)) { loginWindow = true; break; }
            }
        }

        return new GenianStatus(names.Contains("GnAgent"), names.ToList(), loginWindow);
    }

    private static bool LooksLikeLoginWindow(AutomationElement window)
    {
        try { return FindFields(window) is not null; }
        catch (ElementNotAvailableException) { return false; }
    }

    /// <summary>로그인 창의 세 부품: 아이디 칸, 비밀번호 칸, [인증] 버튼.</summary>
    private sealed record LoginFields(AutomationElement UserId, AutomationElement Password, AutomationElement Submit);

    /// <summary>
    /// 로그인 창의 부품을 찾는다. 셋이 다 있어야 로그인 창이다(없으면 null).
    ///
    /// <para>Genian 의 창은 입력 칸을 <c>Edit</c> 종류가 아니라 <b>Pane</b> 으로 내보내고 비밀번호 표시(IsPassword)도 없다
    /// (실제 덤프로 확인). 그래서 "윈도우 클래스가 Edit 인 칸"까지 입력 칸으로 보고,
    /// 비밀번호 칸은 표시나 칸 이름("비밀번호")으로 가른다.</para>
    /// </summary>
    private static LoginFields? FindFields(AutomationElement window)
    {
        AutomationElement? user = null, password = null;
        foreach (AutomationElement e in window.FindAll(TreeScope.Descendants, Condition.TrueCondition))
        {
            var c = e.Current;
            var isInput = c.ControlType == ControlType.Edit
                          || string.Equals(c.ClassName, "Edit", StringComparison.OrdinalIgnoreCase);
            if (!isInput) continue;

            var name = c.Name ?? string.Empty;
            if (c.IsPassword || name.Contains("비밀번호") || name.Contains("password", StringComparison.OrdinalIgnoreCase))
                password ??= e;
            else
                user ??= e;
        }

        var submit = FindSubmit(window);
        return user is null || password is null || submit is null ? null : new LoginFields(user, password, submit);
    }

    /// <summary>
    /// 진단용. Genian 창의 구조(종류·이름·자동화 id·비밀번호 칸 여부)를 글로 뽑는다.
    /// <b>입력된 값은 읽지 않는다.</b> 자동 입력이 안 될 때 창이 어떻게 생겼는지 보려는 용도다.
    /// </summary>
    public static string Dump()
    {
        var sb = new StringBuilder();
        var any = false;
        foreach (var window in GenianWindows())
        {
            try
            {
                sb.AppendLine($"[창] pid={window.Current.ProcessId} class={window.Current.ClassName} name={window.Current.Name}");
                foreach (AutomationElement e in window.FindAll(TreeScope.Descendants, Condition.TrueCondition))
                {
                    var c = e.Current;
                    var patterns = string.Join(",", e.GetSupportedPatterns()
                        .Select(p => p.ProgrammaticName.Replace("PatternIdentifiers.Pattern", string.Empty)));
                    sb.AppendLine($"  {c.ControlType.ProgrammaticName} name='{c.Name}' id='{c.AutomationId}' class='{c.ClassName}' password={c.IsPassword} patterns=[{patterns}]");
                }

                any = true;
            }
            catch (ElementNotAvailableException) { sb.AppendLine("  (창이 사라졌다)"); }
        }

        return any ? sb.ToString() : "Genian 창이 없다.";
    }

    /// <summary>진단 파일 경로. 설정 창의 "창 구조 저장"과 같은 파일이다.</summary>
    public static string DumpPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PortKillerPet", "nac-dump.txt");

    /// <summary>
    /// 창 구조를 진단 파일에 쓴다. 한글이 깨지지 않게 BOM 이 붙은 UTF-8 로 쓴다(메모장·PowerShell 이 바로 읽는다).
    /// 못 써도 조용히 넘어간다 - 진단이 본 기능을 막으면 안 된다.
    /// </summary>
    public static void TryWriteDump()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DumpPath)!);
            File.WriteAllText(DumpPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{Dump()}", new UTF8Encoding(true));
        }
        catch (Exception)
        {
        }
    }

    private static List<AutomationElement> GenianWindows()
    {
        var found = new List<AutomationElement>();
        var names = new Dictionary<int, bool>();
        AutomationElementCollection top;
        try { top = AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition); }
        catch (Exception) { return found; }

        foreach (AutomationElement w in top)
        {
            int pid;
            try { pid = w.Current.ProcessId; }
            catch (ElementNotAvailableException) { continue; }

            if (!names.TryGetValue(pid, out var isGenian))
            {
                isGenian = IsGenianProcess(pid);
                names[pid] = isGenian;
            }

            if (isGenian) found.Add(w);
        }

        return found;
    }

    private static bool IsGenianProcess(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName.StartsWith(ProcessPrefix, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>로그인 창이 아니면 null(계속 찾는다). 로그인 창이면 채워 보고 결과를 돌려준다.</summary>
    private static NacLoginResult? TryFill(AutomationElement window, NacCredential cred)
    {
        try
        {
            var fields = FindFields(window);
            if (fields is null) return null; // 로그인 창이 아직 아니다
            var (userId, password, submit) = (fields.UserId, fields.Password, fields.Submit);

            Set(userId, cred.UserId);
            Set(password, cred.Password);

            // 두 칸 모두 글자 수로 들어갔는지 확인한다(내용은 읽지 않는다). 하나라도 비어 있으면 누르지 않는다 -
            // 빈 비밀번호로 [인증]을 누르면 실패 횟수만 늘어 계정이 잠길 수 있다.
            if (TextLength(userId) is { } idLen && idLen != cred.UserId.Length)
                return new NacLoginResult(NacLoginOutcome.Failed, $"아이디가 제대로 들어가지 않았어요(글자 수 {idLen}).");
            if (TextLength(password) is { } pwLen && pwLen != cred.Password.Length)
                return new NacLoginResult(NacLoginOutcome.Failed, $"비밀번호가 제대로 들어가지 않았어요(글자 수 {pwLen}).");

            ((InvokePattern)submit.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            return new NacLoginResult(NacLoginOutcome.Submitted, "인증 버튼을 눌렀어요.");
        }
        catch (ElementNotAvailableException)
        {
            return null; // 채우는 사이 창이 닫혔다. 다시 찾는다.
        }
        catch (InvalidOperationException ex)
        {
            TryWriteDump();   // 왜 못 넣었는지(지원하는 방식)를 파일로 남긴다
            return new NacLoginResult(NacLoginOutcome.Failed, "입력이 거부됐어요: " + ex.Message);
        }
        catch (UnauthorizedAccessException)
        {
            return new NacLoginResult(NacLoginOutcome.Failed, "이 창은 권한이 높아 입력할 수 없어요.");
        }
    }

    /// <summary>
    /// [인증] 버튼. 이름이 맞는 것만 누른다 - 아무 버튼이나 누르지 않는다(닫기 ×를 누를 수 있다).
    /// 이름을 못 읽는 환경을 위해, Genian 로그인 창(클래스 CN_GNAUTH_DLG)의 [인증] 버튼 고유 번호(1003)도 인정한다.
    /// </summary>
    private static AutomationElement? FindSubmit(AutomationElement window)
    {
        var isGenianDialog = window.Current.ClassName == "CN_GNAUTH_DLG";
        foreach (AutomationElement b in window.FindAll(TreeScope.Descendants,
                     new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
        {
            var c = b.Current;
            if (c.Name.Trim() == SubmitButtonName) return b;
            if (isGenianDialog && c.AutomationId == "1003") return b;
        }

        return null;
    }

    // ---------- 글자 넣기 ----------
    //
    // Genian 로그인 창의 입력 칸은 UI 자동화의 값 넣기(Value)를 지원하지 않는다(실제 덤프: patterns=[]).
    // 그래서 칸의 창 핸들에 "이 글자로 바꿔라"(WM_SETTEXT) 메시지를 보낸다. 키 입력을 흉내 내는 것이 아니라
    // 그 칸 하나를 지정해서 넣으므로, 포커스가 다른 창으로 가 있어도 엉뚱한 곳에 입력되지 않는다.

    private const int WmSetText = 0x000C;
    private const int WmGetTextLength = 0x000E;
    private const int ErrorAccessDenied = 5;

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageText(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", SetLastError = true)]
    private static extern IntPtr SendMessagePtr(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// 칸에 글자를 넣는다. 창 핸들이 있으면 WM_SETTEXT 를 먼저 쓴다(Genian 칸은 이것만 된다).
    /// 핸들이 없는 칸(웹·WPF 등)만 UI 자동화의 값 넣기로 넣는다.
    /// </summary>
    private static void Set(AutomationElement edit, string value)
    {
        var hwnd = new IntPtr(edit.Current.NativeWindowHandle);
        if (hwnd != IntPtr.Zero)
        {
            if (SendMessageText(hwnd, WmSetText, IntPtr.Zero, value) != IntPtr.Zero) return;

            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(error == ErrorAccessDenied
                ? "Genian 창이 관리자 권한이라 글자를 넣을 수 없어요."
                : $"글자를 넣지 못했어요(오류 {error}).");
        }

        if (edit.TryGetCurrentPattern(ValuePattern.Pattern, out var p))
        {
            ((ValuePattern)p).SetValue(value);
            return;
        }

        throw new InvalidOperationException("값을 넣을 수 없는 칸이에요(창 핸들도 값 넣기도 없음).");
    }

    /// <summary>
    /// 칸에 들어 있는 글자 수. 내용은 읽지 않는다(창 핸들이 있으면 글자 수만 묻는다). 알 수 없으면 null.
    /// 비밀번호 칸은 UI 자동화로 값을 읽으면 가려진 값이 나와서 쓰지 않는다.
    /// </summary>
    private static int? TextLength(AutomationElement edit)
    {
        var hwnd = new IntPtr(edit.Current.NativeWindowHandle);
        if (hwnd != IntPtr.Zero) return (int)SendMessagePtr(hwnd, WmGetTextLength, IntPtr.Zero, IntPtr.Zero);

        if (!edit.Current.IsPassword && edit.TryGetCurrentPattern(ValuePattern.Pattern, out var p))
            return ((ValuePattern)p).Current.Value?.Length;

        return null;
    }

    // ---------- 결과 기록 ----------

    /// <summary>
    /// 자동 로그인 결과를 <c>%APPDATA%\PortKillerPet\nac-log.txt</c> 에 한 줄 남긴다(시각·결과·이유).
    /// 아이디·비밀번호는 쓰지 않는다. 말풍선은 금방 사라지므로 재부팅 뒤에 무슨 일이 있었는지 보려는 것이다.
    /// </summary>
    private static NacLoginResult Logged(NacLoginResult result)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PortKillerPet", "nac-log.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {result.Outcome} {result.Message}{Environment.NewLine}",
                new UTF8Encoding(true));
        }
        catch (Exception)
        {
        }

        return result;
    }
}
