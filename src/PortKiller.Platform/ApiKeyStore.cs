using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PortKiller.Core;

namespace PortKiller.Platform;

/// <summary>
/// AI 열쇠를 <b>암호화해서</b> 내 PC 사용자 폴더에만 둔다.
///
/// <para>왜 평문이면 안 되나: 이건 사내에 뿌리는 프로그램이고, 열쇠는 돈이 나가는 물건이다.
/// 평문으로 두면 같은 PC 를 쓰는 다른 계정, 백업 도구, 실수로 공유된 폴더를 타고 그대로 샌다.</para>
///
/// <para>윈도우 DPAPI(<see cref="ProtectedData"/>)를 쓴다. <b>현재 사용자</b> 범위라,
/// 그 파일을 다른 PC 나 다른 계정으로 옮기면 복호화가 아예 안 된다 — 파일만 훔쳐도 소용이 없다.
/// 열쇠를 따로 관리할 필요도 없다(윈도우 로그인 자격이 열쇠 역할을 한다).</para>
///
/// <para>세이브(<c>save.json</c>)와 <b>다른 파일</b>에 둔다. 섞어 두면 세이브를 보내 달라거나
/// 백업하라고 할 때 열쇠가 딸려 나간다. 펫을 처음부터 다시 키워도 열쇠는 남아야 한다.</para>
/// </summary>
public sealed class ApiKeyStore
{
    /// <summary>
    /// 암호문에 함께 묶는 꼬리표. 다른 프로그램이 만든 DPAPI 덩어리를 우리 것으로 착각해
    /// 풀려고 드는 일을 막는다(맞지 않으면 복호화가 실패한다).
    /// </summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PortKillerPet.ApiKeys.v1");

    private readonly string _path;

    public ApiKeyStore(string? directory = null)
    {
        var dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PortKillerPet");
        _path = Path.Combine(dir, "keys.dat");
    }

    /// <summary>등록된 열쇠가 하나라도 있나. 값은 읽지 않는다 — 화면 표시에만 쓴다.</summary>
    public bool Has(AiProvider provider) => !string.IsNullOrWhiteSpace(Get(provider));

    /// <summary>
    /// 열쇠를 꺼낸다. 없거나 못 풀면 null 이다.
    /// <b>못 푸는 것은 정상 경로다</b> — 윈도우 계정이 바뀌거나 파일을 옮겨 오면 그렇게 된다.
    /// 그때는 등록이 안 된 것으로 보고 다시 등록하게 한다.
    /// </summary>
    public string? Get(AiProvider provider)
    {
        var all = ReadAll();
        return all.TryGetValue(provider.ToString(), out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    /// <summary>등록·교체. 빈 값을 주면 지운다.</summary>
    public void Set(AiProvider provider, string? key)
    {
        var all = ReadAll();
        if (string.IsNullOrWhiteSpace(key)) all.Remove(provider.ToString());
        else all[provider.ToString()] = key.Trim();
        WriteAll(all);
    }

    /// <summary>등록한 열쇠를 전부 지운다. 파일 자체를 없앤다.</summary>
    public void ClearAll()
    {
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
        catch (IOException)
        {
            // 지우지 못했으면 빈 내용으로 덮어써서라도 값을 없앤다.
            WriteAll(new Dictionary<string, string>());
        }
    }

    private Dictionary<string, string> ReadAll()
    {
        try
        {
            if (!File.Exists(_path)) return new Dictionary<string, string>();

            var plain = ProtectedData.Unprotect(
                File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(plain))
                   ?? new Dictionary<string, string>();
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException
                                      or UnauthorizedAccessException)
        {
            // 다른 계정이 만든 파일이거나 깨진 파일이다. 등록 안 된 것으로 본다.
            return new Dictionary<string, string>();
        }
    }

    private void WriteAll(Dictionary<string, string> all)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        var cipher = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(all)), Entropy, DataProtectionScope.CurrentUser);

        // 세이브와 같은 방식: 임시 파일에 쓰고 이름을 바꾼다. 쓰다가 꺼져도 기존 열쇠가 살아남는다.
        var tmp = _path + ".tmp";
        File.WriteAllBytes(tmp, cipher);
        File.Move(tmp, _path, overwrite: true);
    }
}
