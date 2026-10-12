using System.IO;

namespace PortKiller.App;

/// <summary>
/// 시작·종료·오류를 <c>%APPDATA%\PortKillerPet\startup.log</c> 에 한 줄씩 남긴다.
///
/// <para>왜 필요한가: 재부팅 뒤 "자동으로 안 떴다"는 일이 생겼을 때, <b>윈도우가 아예 실행하지 않은 것</b>인지
/// <b>실행했는데 곧바로 죽거나 막힌 것</b>인지 가를 길이 없었다. 이 파일에 시작 줄이 있으면 실행은 된 것이다.</para>
///
/// <para>남기는 것은 시각·프로세스 번호·실행 인자·(예외가 났다면) 예외 종류와 메시지뿐이다.
/// 저장 파일이 꽉 차는 일을 막으려고 크기가 커지면 앞부분을 잘라 낸다.</para>
/// </summary>
internal static class StartupLog
{
    private const long MaxBytes = 64 * 1024;

    private static string PathOf()
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PortKillerPet");
        Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, "startup.log");
    }

    public static void Write(string message)
    {
        try
        {
            var path = PathOf();
            if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
            {
                var keep = File.ReadAllLines(path);
                File.WriteAllLines(path, keep.Skip(keep.Length / 2));
            }

            File.AppendAllText(path,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} pid={Environment.ProcessId} {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // 기록을 못 남겨도 앱은 계속 떠야 한다.
        }
    }

    public static void Crash(string where, Exception ex)
        => Write($"CRASH({where}) {ex.GetType().Name}: {ex.Message}");
}
