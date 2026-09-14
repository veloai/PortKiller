using System.Text.Json;
using PortKiller.Core;

namespace PortKiller.Platform;

/// <summary>
/// 세이브를 내 PC 사용자 폴더에만 남긴다. 밖으로 나가는 통신은 이 앱 전체에 없다.
/// 폴더를 지우면 흔적이 0이 된다.
///
/// 쓰기는 "임시 파일에 쓰고 → 이름 바꾸기" 순서로 한다.
/// 저장 도중에 꺼져도 반쯤 쓰다 만 파일을 다음에 읽는 일이 없게 하려는 것이다.
/// 직전 세이브는 .bak 으로 한 세대 남긴다.
/// </summary>
public sealed class JsonSaveStore : ISaveStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    private readonly string _dir;
    private readonly string _path;
    private readonly string _backupPath;

    public JsonSaveStore(string? directory = null)
    {
        _dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PortKillerPet");
        _path = Path.Combine(_dir, "save.json");
        _backupPath = Path.Combine(_dir, "save.bak");
    }

    public SaveData Load()
    {
        foreach (var candidate in new[] { _path, _backupPath })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var json = File.ReadAllText(candidate);
                var data = JsonSerializer.Deserialize<SaveData>(json, Options);
                if (data is not null) return data;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // 깨진 세이브는 조용히 건너뛰고 백업을 시도한다. 그것도 없으면 새로 시작한다.
            }
        }
        return new SaveData();
    }

    public void Save(SaveData data)
    {
        Directory.CreateDirectory(_dir);
        var tmp = _path + ".tmp";

        File.WriteAllText(tmp, JsonSerializer.Serialize(data, Options));

        if (File.Exists(_path)) File.Copy(_path, _backupPath, overwrite: true);
        File.Move(tmp, _path, overwrite: true);
    }
}
