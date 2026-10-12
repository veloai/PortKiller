using System.Threading;
using System.Windows;

namespace PortKiller.App;

public partial class App : Application
{
    /// <summary>
    /// 이름 있는 뮤텍스. 같은 윈도우 계정에서 두 번째로 켜면 조용히 끝난다.
    /// 자동 실행을 두 경로(시작 프로그램 등록 + 시작 폴더)로 걸어 두면 둘 다 떠서 펫이 두 마리가 되기 때문이다.
    /// </summary>
    private const string SingleInstanceName = "Local\\PortKillerPet.SingleInstance";

    private Mutex? _singleInstance;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        StartupLog.Write("start args=[" + string.Join(" ", e.Args) + "]");

        DispatcherUnhandledException += (_, ex) => StartupLog.Crash("ui", ex.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            StartupLog.Crash("domain", ex.ExceptionObject as Exception ?? new InvalidOperationException("알 수 없는 오류"));

        // 검사용 옵션(--save-dir, --render-sheet 등)이 붙은 실행은 중복을 허용한다.
        // 그래야 진짜 펫이 켜져 있어도 다른 저장 폴더로 시험을 돌릴 수 있다.
        var isTestRun = e.Args.Any(a => a.StartsWith("--", StringComparison.Ordinal));
        if (!isTestRun)
        {
            _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceName, out var created);
            _ownsMutex = created;
            if (!created)
            {
                StartupLog.Write("already running - exit");
                Shutdown();
                return;
            }
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StartupLog.Write("exit code=" + e.ApplicationExitCode);
        if (_ownsMutex) _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
