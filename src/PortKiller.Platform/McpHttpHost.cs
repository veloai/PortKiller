using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using PortKiller.Core;

namespace PortKiller.Platform;

/// <summary>
/// MCP 서버의 HTTP 입구. <b>이 PC 안(127.0.0.1)에서만</b> 열리고, 열쇠(Bearer 토큰)가 맞아야 받는다.
///
/// <para>지키는 것:
/// ① 루프백 주소에만 바인딩 - 다른 PC 에서는 닿지 않는다.
/// ② <c>Authorization: Bearer &lt;열쇠&gt;</c> 가 맞아야 처리한다 - 같은 PC 의 다른 프로그램이 함부로 펫을 조종하지 못하게.
/// ③ 브라우저가 붙이는 <c>Origin</c> 이 localhost 가 아니면 거절한다 - 웹페이지가 몰래 부르는 공격(DNS 리바인딩)을 막는다.
/// ④ 본문 크기·읽기 시간 제한.</para>
///
/// <para>외부 라이브러리 없이 TcpListener 로 HTTP/1.1 요청 한 개씩 처리한다(요청마다 연결을 닫는다).
/// MCP 의 "Streamable HTTP" 중 JSON 응답만 쓴다 - 서버가 먼저 보내는 알림(SSE)은 없다.</para>
/// </summary>
public sealed class McpHttpHost : IDisposable
{
    public const int DefaultPort = 47420;
    private const int MaxBodyBytes = 1024 * 1024;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    private readonly McpRouter _router;
    private readonly byte[] _token;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();

    public int Port { get; }

    /// <summary>열쇠 파일 위치. 처음 켤 때 무작위로 만든다. MCP 클라이언트 설정에 이 값을 넣는다.</summary>
    public static string KeyPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PortKillerPet", "mcp.key");

    public McpHttpHost(McpRouter router, string token, int port = DefaultPort)
    {
        _router = router;
        _token = Encoding.UTF8.GetBytes(token);
        Port = port;
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    /// <summary>열쇠를 읽는다. 없으면 만들어 둔다(32바이트 무작위, 16진수).</summary>
    public static string LoadOrCreateKey()
    {
        if (File.Exists(KeyPath))
        {
            var existing = File.ReadAllText(KeyPath).Trim();
            if (existing.Length >= 32) return existing;
        }

        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        Directory.CreateDirectory(Path.GetDirectoryName(KeyPath)!);
        File.WriteAllText(KeyPath, key);
        return key;
    }

    /// <summary>받기 시작한다. 포트가 이미 쓰이고 있으면 false(앱은 MCP 없이 계속 돈다).</summary>
    public bool Start()
    {
        try
        {
            _listener.Start();
        }
        catch (SocketException)
        {
            return false;
        }

        _ = AcceptLoopAsync(_cts.Token);
        return true;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(ct);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            _ = Task.Run(() => ServeAsync(client, ct), ct);
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ReadTimeout);
            var stream = client.GetStream();

            try
            {
                var request = await ReadRequestAsync(stream, timeout.Token);
                if (request is null)
                {
                    await WriteAsync(stream, 400, "text/plain", "bad request");
                    return;
                }

                var (method, path, headers, body) = request.Value;

                if (headers.TryGetValue("origin", out var origin) && !IsLocalOrigin(origin))
                {
                    await WriteAsync(stream, 403, "text/plain", "forbidden origin");
                    return;
                }

                if (!Authorized(headers))
                {
                    await WriteAsync(stream, 401, "text/plain", "unauthorized");
                    return;
                }

                if (!path.StartsWith("/mcp", StringComparison.Ordinal))
                {
                    await WriteAsync(stream, 404, "text/plain", "not found");
                    return;
                }

                if (method != "POST")
                {
                    // 서버가 먼저 보내는 스트림(GET)은 쓰지 않는다. 스펙대로 405 로 알린다.
                    await WriteAsync(stream, 405, "text/plain", "method not allowed");
                    return;
                }

                var response = _router.Handle(body);
                if (response is null) await WriteAsync(stream, 202, "text/plain", string.Empty);
                else await WriteAsync(stream, 200, "application/json", response);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException)
            {
                // 끊긴 연결·시간 초과. 그냥 닫는다.
            }
        }
    }

    private bool Authorized(IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue("authorization", out var auth)) return false;
        const string prefix = "Bearer ";
        if (!auth.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var given = Encoding.UTF8.GetBytes(auth[prefix.Length..].Trim());
        return CryptographicOperations.FixedTimeEquals(given, _token);
    }

    private static bool IsLocalOrigin(string origin)
    {
        if (string.Equals(origin, "null", StringComparison.Ordinal)) return false;
        return Uri.TryCreate(origin, UriKind.Absolute, out var u)
               && (u.Host is "localhost" or "127.0.0.1" or "[::1]");
    }

    /// <summary>HTTP 요청 하나를 읽는다: 요청 줄, 머리글, Content-Length 만큼의 본문.</summary>
    private static async Task<(string Method, string Path, Dictionary<string, string> Headers, string Body)?> ReadRequestAsync(
        NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var head = new MemoryStream();
        int headerEnd;
        while (true)
        {
            var n = await stream.ReadAsync(buffer, ct);
            if (n == 0) return null;
            head.Write(buffer, 0, n);
            headerEnd = IndexOfHeaderEnd(head.GetBuffer(), (int)head.Length);
            if (headerEnd >= 0) break;
            if (head.Length > 64 * 1024) return null;
        }

        var all = head.GetBuffer();
        var headerText = Encoding.ASCII.GetString(all, 0, headerEnd);
        var lines = headerText.Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length < 2) return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0) headers[line[..colon].Trim().ToLowerInvariant()] = line[(colon + 1)..].Trim();
        }

        var length = headers.TryGetValue("content-length", out var cl) && int.TryParse(cl, out var len) ? len : 0;
        if (length < 0 || length > MaxBodyBytes) return null;

        var bodyStart = headerEnd + 4;
        var body = new MemoryStream();
        body.Write(all, bodyStart, (int)head.Length - bodyStart);
        while (body.Length < length)
        {
            var n = await stream.ReadAsync(buffer, ct);
            if (n == 0) break;
            body.Write(buffer, 0, n);
        }

        var bodyText = Encoding.UTF8.GetString(body.GetBuffer(), 0, (int)Math.Min(body.Length, length));
        return (first[0].ToUpperInvariant(), first[1], headers, bodyText);
    }

    private static int IndexOfHeaderEnd(byte[] data, int length)
    {
        for (var i = 0; i + 3 < length; i++)
        {
            if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n') return i;
        }

        return -1;
    }

    private static async Task WriteAsync(NetworkStream stream, int status, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var reason = status switch
        {
            200 => "OK", 202 => "Accepted", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden",
            404 => "Not Found", 405 => "Method Not Allowed", _ => "Error",
        };
        var head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}; charset=utf-8\r\nContent-Length: {bytes.Length}\r\n" +
                   (status == 405 ? "Allow: POST\r\n" : string.Empty) + "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch (SocketException) { }
        _cts.Dispose();
    }
}
