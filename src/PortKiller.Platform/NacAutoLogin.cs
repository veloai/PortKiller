using System.Diagnostics;
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
                if (result is not null) return result;
            }

            if (DateTime.UtcNow >= deadline) break;
            if (ct.WaitHandle.WaitOne(PollInterval)) break;
        }

        return new NacLoginResult(NacLoginOutcome.NotFound, "로그인 창이 보이지 않았어요.");
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
                    sb.AppendLine($"  {c.ControlType.ProgrammaticName} name='{c.Name}' id='{c.AutomationId}' class='{c.ClassName}' password={c.IsPassword}");
                }

                any = true;
            }
            catch (ElementNotAvailableException) { sb.AppendLine("  (창이 사라졌다)"); }
        }

        return any ? sb.ToString() : "Genian 창이 없다.";
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
            var edits = window.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

            AutomationElement? password = null, userId = null;
            foreach (AutomationElement e in edits)
            {
                if (e.Current.IsPassword) password ??= e;
                else userId ??= e;
            }

            var submit = FindSubmit(window);
            if (password is null || userId is null || submit is null) return null; // 로그인 창이 아직 아니다

            Set(userId, cred.UserId);
            Set(password, cred.Password);

            // 아이디 칸은 읽어서 들어갔는지 확인한다. 비밀번호 칸은 윈도우가 읽기를 막으므로 확인하지 않는다.
            if (userId.TryGetCurrentPattern(ValuePattern.Pattern, out var vp)
                && ((ValuePattern)vp).Current.Value != cred.UserId)
            {
                return new NacLoginResult(NacLoginOutcome.Failed, "아이디가 입력되지 않았어요.");
            }

            ((InvokePattern)submit.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            return new NacLoginResult(NacLoginOutcome.Submitted, "인증 버튼을 눌렀어요.");
        }
        catch (ElementNotAvailableException)
        {
            return null; // 채우는 사이 창이 닫혔다. 다시 찾는다.
        }
        catch (InvalidOperationException ex)
        {
            return new NacLoginResult(NacLoginOutcome.Failed, "입력이 거부됐어요: " + ex.Message);
        }
        catch (UnauthorizedAccessException)
        {
            return new NacLoginResult(NacLoginOutcome.Failed, "이 창은 권한이 높아 입력할 수 없어요.");
        }
    }

    /// <summary>[인증] 버튼. 이름이 맞는 것만 누른다 — 아무 버튼이나 누르지 않는다(닫기 ×를 누를 수 있다).</summary>
    private static AutomationElement? FindSubmit(AutomationElement window)
    {
        foreach (AutomationElement b in window.FindAll(TreeScope.Descendants,
                     new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
        {
            if (b.Current.Name.Trim() == SubmitButtonName) return b;
        }

        return null;
    }

    private static void Set(AutomationElement edit, string value)
    {
        if (!edit.TryGetCurrentPattern(ValuePattern.Pattern, out var p))
            throw new InvalidOperationException("값을 넣을 수 없는 칸이에요.");
        ((ValuePattern)p).SetValue(value);
    }
}
