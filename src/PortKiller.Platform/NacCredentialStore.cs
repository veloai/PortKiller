using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PortKiller.Platform;

/// <summary>Genian NAC 로그인에 쓸 아이디·비밀번호와 켜짐 여부.</summary>
public sealed record NacCredential(string UserId, string Password, bool Enabled);

/// <summary>
/// NAC 아이디·비밀번호를 <b>암호화해서</b> 내 계정 폴더에만 둔다. <see cref="ApiKeyStore"/> 와 같은 방식이다.
///
/// <para>윈도우 DPAPI 현재 사용자 범위라 파일을 다른 PC·다른 계정으로 옮기면 풀리지 않는다.
/// 꼬리표(entropy)는 열쇠 파일과 다르게 둔다 — 서로의 파일을 풀 수 없게.</para>
///
/// <para>세이브·AI 설정과 <b>다른 파일</b>(<c>nac.dat</c>)이다. 펫을 초기화해도 남고,
/// 세이브를 남에게 보내도 비밀번호가 딸려 가지 않는다.</para>
/// </summary>
public sealed class NacCredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PortKillerPet.Nac.v1");

    private readonly string _path;

    public NacCredentialStore(string? directory = null)
    {
        var dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PortKillerPet");
        _path = Path.Combine(dir, "nac.dat");
    }

    /// <summary>등록돼 있으면 꺼낸다. 없거나 못 풀면 null — 못 푸는 것은 정상 경로다(계정이 바뀐 경우).</summary>
    public NacCredential? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;

            var plain = ProtectedData.Unprotect(
                File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
            var cred = JsonSerializer.Deserialize<NacCredential>(Encoding.UTF8.GetString(plain));
            return cred is { UserId.Length: > 0, Password.Length: > 0 } ? cred : null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException
                                      or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(NacCredential cred)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        var cipher = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cred)), Entropy, DataProtectionScope.CurrentUser);

        var tmp = _path + ".tmp";
        File.WriteAllBytes(tmp, cipher);
        File.Move(tmp, _path, overwrite: true);
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
        catch (IOException)
        {
            // 못 지웠으면 쓸 수 없는 상태로 덮어써서라도 값을 없앤다.
            File.WriteAllBytes(_path, Array.Empty<byte>());
        }
    }
}
