using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PortKiller.Platform;

/// <summary>
/// "자리에 있었는가"만 측정한다.
///
/// 개인정보·보안 설계 (사내 배포 전제):
///   - 어떤 키를 눌렀는지 읽지 않는다. 읽을 수 있는 함수를 아예 부르지 않는다.
///   - 마우스가 어디를 클릭했는지 읽지 않는다.
///   - 쓰는 윈도우 함수는 GetLastInputInfo 하나뿐이다.
///     이 함수는 "마지막 입력 이후 몇 밀리초 지났는가"라는 숫자 하나만 돌려준다.
///   - 전역 키보드 훅(SetWindowsHookEx)을 쓰지 않는다. 백신·EDR 이 가장 민감하게 보는 방식이다.
///
/// 결과적으로 이 클래스가 만들어내는 값은 "집중한 초" 하나뿐이다.
/// </summary>
public sealed class ActivityMonitor : IDisposable
{
    /// <summary>이만큼 입력이 없으면 자리를 비운 것으로 본다.</summary>
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(1);

    private readonly System.Threading.Timer _timer;
    private readonly object _gate = new();
    private readonly TimeSpan _interval;

    private TimeSpan _focused = TimeSpan.Zero;

    /// <summary>마지막으로 읽어간 뒤 새로 쌓인 집중 시간. 읽으면 0으로 비워진다.</summary>
    public TimeSpan DrainFocused()
    {
        lock (_gate)
        {
            var v = _focused;
            _focused = TimeSpan.Zero;
            return v;
        }
    }

    public bool IsIdle { get; private set; }

    public ActivityMonitor(TimeSpan? pollInterval = null)
    {
        _interval = pollInterval ?? TimeSpan.FromSeconds(5);
        _timer = new System.Threading.Timer(_ => Poll(), null, _interval, _interval);
    }

    private void Poll()
    {
        var idle = GetIdleTime();
        IsIdle = idle >= IdleThreshold;
        if (IsIdle) return;

        lock (_gate) _focused += _interval;
    }

    /// <summary>마지막 입력 이후 지난 시간. 입력의 내용은 알 수 없고 알 필요도 없다.</summary>
    public static TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;

        var elapsedMs = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(elapsedMs);
    }

    public void Dispose() => _timer.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
}
