using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PortKiller.Platform;

/// <summary>
/// 시작할 때 자동 실행 등록.
///
/// 기본은 꺼짐이다. 절대 자동으로 켜지 않는다.
/// 사내 PC 에 배포되는 프로그램이 조용히 시작 프로그램에 자기를 등록하는 것은
/// 보안 검토에서 바로 걸리는 행동이고, 실제로 악성코드가 하는 짓과 구분되지 않는다.
/// 사용자가 트레이 메뉴나 NAC 설정 창에서 직접 켤 때만 이 클래스가 호출된다.
/// </summary>
public static class AutostartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PortKillerPet";

    /// <summary>
    /// 시작 폴더 바로가기. 레지스트리 항목(Run)만으로는 재부팅 때 안 뜨는 PC 가 있어서(실제로 그랬다),
    /// 같은 일을 윈도우의 다른 길(시작 폴더)로도 건다. 둘 다 떠도 앱이 한 번만 뜨도록 앱 쪽에서 막아 둔다.
    /// </summary>
    private static string StartupShortcutPath
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "PortKillerPet.lnk");

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return (key?.GetValue(ValueName) is string s && s.Length > 0) || File.Exists(StartupShortcutPath);
    }

    public static void Enable()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) throw new InvalidOperationException("실행 파일 경로를 찾지 못했다.");

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(ValueName, $"\"{exe}\"", RegistryValueKind.String);

        TryCreateShortcut(exe);
    }

    public static void Disable()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
        {
            if (key?.GetValue(ValueName) is not null) key.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        try
        {
            if (File.Exists(StartupShortcutPath)) File.Delete(StartupShortcutPath);
        }
        catch (IOException)
        {
            // 지우지 못해도 레지스트리 쪽은 이미 껐다. 다음에 다시 시도하면 된다.
        }
    }

    /// <summary>
    /// 바로가기는 윈도우 기본 부품(WScript.Shell)으로 만든다. 못 만들어도 레지스트리 등록은 이미 됐으므로 조용히 넘어간다.
    /// </summary>
    private static void TryCreateShortcut(string exe)
    {
        object? shell = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return;

            shell = Activator.CreateInstance(type);
            dynamic link = ((dynamic)shell!).CreateShortcut(StartupShortcutPath);
            link.TargetPath = exe;
            link.WorkingDirectory = Path.GetDirectoryName(exe);
            link.Description = "PortKiller Pet";
            link.Save();
        }
        catch (Exception)
        {
            // 정책으로 막히거나 COM 이 없으면 시작 폴더 쪽은 건너뛴다.
        }
        finally
        {
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
    }
}
