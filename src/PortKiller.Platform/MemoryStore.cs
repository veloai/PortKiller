using System.Text.Encodings.Web;
using System.Text.Json;
using PortKiller.Core;

namespace PortKiller.Platform;

/// <summary>
/// 펫의 장기 기억(<c>%APPDATA%\PortKillerPet\memory.json</c>). 세이브와 따로 둔다 -
/// 새 펫을 맞이해도 "나"에 대한 기억은 남고, 기억만 따로 지울 수도 있다.
/// </summary>
public sealed class MemoryStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path;

    public MemoryStore(string? directory = null)
    {
        var dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PortKillerPet");
        _path = Path.Combine(dir, "memory.json");
    }

    public string FilePath => _path;

    public PetMemory Load()
    {
        try
        {
            if (File.Exists(_path))
                return new PetMemory(JsonSerializer.Deserialize<MemoryData>(File.ReadAllText(_path), Json));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // 깨진 기억 파일 하나로 앱이 못 뜨면 안 된다. 빈 기억으로 시작한다.
        }

        return new PetMemory();
    }

    public void Save(PetMemory memory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(memory.Data, Json));
        File.Move(tmp, _path, overwrite: true);
    }

    /// <summary>기억을 모두 지운다(빈 기억으로 덮어쓴다).</summary>
    public PetMemory Clear()
    {
        var empty = new PetMemory();
        Save(empty);
        return empty;
    }
}
