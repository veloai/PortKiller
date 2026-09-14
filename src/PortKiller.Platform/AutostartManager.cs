using Microsoft.Win32;

namespace PortKiller.Platform;

/// <summary>
/// 시작할 때 자동 실행 등록.
///
/// 기본은 꺼짐이다. 절대 자동으로 켜지 않는다.
/// 사내 PC 에 배포되는 프로그램이 조용히 시작 프로그램에 자기를 등록하는 것은
/// 보안 검토에서 바로 걸리는 행동이고, 실제로 악성코드가 하는 짓과 구분되지 않는다.
/// 사용자가 트레이 메뉴에서 직접 켤 때만 이 클래스가 호출된다.
/// </summary>
public static class AutostartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PortKillerPet";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string s && s.Length > 0;
    }

    public static void Enable()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) throw new InvalidOperationException("실행 파일 경로를 찾지 못했다.");

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(ValueName, $"\"{exe}\"", RegistryValueKind.String);
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key?.GetValue(ValueName) is not null) key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
