using System.Text.Json;
using PortKiller.Core;

namespace PortKiller.Platform;

/// <summary>
/// AI 설정(어느 곳을 쓸지·모델 이름·말투)을 평문 JSON 으로 둔다. 비밀이 없는 값들이다.
/// 열쇠는 여기 오지 않는다 — <see cref="ApiKeyStore"/> 가 따로 암호화해 보관한다.
///
/// <para>세이브와 파일을 나눈 이유: "처음부터 다시 키우기"로 펫을 초기화해도
/// AI 설정과 열쇠는 남아야 한다. 같은 파일에 두면 한 번에 날아간다.</para>
/// </summary>
public sealed class AiSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;

    public AiSettingsStore(string? directory = null)
    {
        var dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PortKillerPet");
        _path = Path.Combine(dir, "ai.json");
    }

    public AiSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AiSettings();
            return JsonSerializer.Deserialize<AiSettings>(File.ReadAllText(_path), Options)
                   ?? new AiSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // 깨진 설정 하나로 앱이 못 뜨면 안 된다. 기본값으로 시작한다.
            return new AiSettings();
        }
    }

    public void Save(AiSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
        File.Move(tmp, _path, overwrite: true);
    }
}
