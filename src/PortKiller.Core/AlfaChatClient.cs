using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PortKiller.Core;

/// <summary>
/// ALFA 백엔드의 비서(Alorys)에게 묻는다. 계약은 <c>POST /api/alorys/chat</c>
/// 본문 <c>{"text": "...", "history": []}</c>, 머리글 <c>Authorization: Bearer &lt;ALFA 로그인 열쇠&gt;</c>.
/// 답은 <c>{"reply": "...", "source": "alorys|alorys-ai", "status": "completed|approval_required"}</c>.
///
/// <para>주소는 <b>이 PC 의 ALFA(127.0.0.1)만</b> 받는다. 열쇠가 ALFA 주인(master) 권한이라
/// 설정 오타로 다른 곳에 보내지면 그대로 새기 때문이다.</para>
///
/// <para>말투(Persona)는 보내지 않는다 — ALFA 가 자기 규칙으로 답한다. 펫은 입구일 뿐이다.
/// 관리 동작이 승인을 기다리면(<c>approval_required</c>) 그 사실을 그대로 보여 주고, 펫이 대신 승인하지 않는다.</para>
/// </summary>
public sealed class AlfaChatClient : IAiClient
{
    public const string DefaultBaseUrl = "http://127.0.0.1:7701";

    /// <summary>서버가 받는 질문 길이 한도(<c>AlorysChatController.valid</c> 와 같게).</summary>
    public const int MaxTextLength = 4000;

    private readonly HttpClient _http;
    private readonly Func<string?> _token;
    private readonly AiSettings _settings;

    public AlfaChatClient(HttpClient http, Func<string?> token, AiSettings settings)
    {
        _http = http;
        _token = token;
        _settings = settings;
    }

    public AiProvider Provider => AiProvider.Alfa;

    /// <summary>설정 주소가 이 PC 를 가리키는지. 아니면 보내지 않는다.</summary>
    public static bool IsLocal(string? baseUrl)
        => Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
           && uri.Scheme is "http" or "https"
           && (uri.IsLoopback || uri.Host == "localhost");

    public async Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt)) throw new AiException("무엇을 물어볼지 적어 주세요.");
        prompt = prompt.Trim();
        if (prompt.Length > MaxTextLength)
            throw new AiException($"질문이 너무 길어요. {MaxTextLength}자 안으로 줄여 주세요.");

        var token = _token();
        if (string.IsNullOrWhiteSpace(token))
            throw new AiException("ALFA 로그인이 필요해요. 트레이 메뉴의 'AI 설정'에서 ALFA 에 로그인해 주세요.");

        var baseUrl = BaseUrl(_settings);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/alorys/chat")
        {
            Content = JsonContent.Create(new JsonObject { ["text"] = prompt, ["history"] = new JsonArray() }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw Transport(ex, cancellationToken);
        }

        using (response)
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw FromError(response.StatusCode, ErrorCode(json));
            return ReadAnswer(json);
        }
    }

    /// <summary>
    /// 아이디·비밀번호로 ALFA 에 로그인해 열쇠(세션, 7일)를 받는다. <b>비밀번호는 여기서 보내고 끝</b>이다 —
    /// 펫은 비밀번호를 저장하지 않고 돌려받은 열쇠만 보관한다.
    /// </summary>
    public static async Task<string> LoginAsync(HttpClient http, string baseUrl, string username, string password,
                                                CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            throw new AiException("아이디와 비밀번호를 적어 주세요.");
        if (!IsLocal(baseUrl)) throw new AiException("ALFA 주소는 이 PC(127.0.0.1)만 쓸 수 있어요.");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/api/auth/login")
        {
            Content = JsonContent.Create(new JsonObject { ["username"] = username.Trim(), ["password"] = password }),
        };

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw Transport(ex, cancellationToken);
        }

        using (response)
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // 본문은 보여 주지 않는다. 단, 패스키 전용 계정은 할 일이 달라서 알려 준다.
                throw new AiException(json.Contains("passkey", StringComparison.OrdinalIgnoreCase)
                    ? "이 계정은 패스키로만 로그인돼요. 웹에서 로그인한 뒤 열쇠를 직접 붙여 넣어 주세요."
                    : "아이디나 비밀번호가 맞지 않아요.");
            }
            if (!response.IsSuccessStatusCode) throw FromError(response.StatusCode, null);

            try
            {
                var token = JsonNode.Parse(json)?["token"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(token)) throw new AiException("ALFA 가 열쇠를 돌려주지 않았어요.");
                return token;
            }
            catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
            {
                throw new AiException("ALFA 로그인 응답을 읽지 못했어요.", ex);
            }
        }
    }

    private static string BaseUrl(AiSettings settings)
    {
        var url = string.IsNullOrWhiteSpace(settings.AlfaBaseUrl) ? DefaultBaseUrl : settings.AlfaBaseUrl.Trim();
        if (!IsLocal(url)) throw new AiException("ALFA 주소는 이 PC(127.0.0.1)만 쓸 수 있어요. 설정을 확인해 주세요.");
        return url.TrimEnd('/');
    }

    /// <summary>오류 본문은 <c>{"error":"CODE"}</c> 뿐이다. 코드만 꺼내고 나머지는 버린다.</summary>
    private static string? ErrorCode(string json)
    {
        try
        {
            return JsonNode.Parse(json)?["error"]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>ALFA 가 돌려준 코드를 사람 말로. 본문을 그대로 붙이지 않는다.</summary>
    public static AiException FromError(HttpStatusCode status, string? code) => code switch
    {
        "AUTHENTICATION_REQUIRED" => new("ALFA 로그인이 끝났어요(7일). 'AI 설정'에서 다시 로그인해 주세요."),
        "AUTHORIZATION_REQUIRED" => new("이 ALFA 계정은 비서에게 물을 권한이 없어요(주인 계정만 돼요)."),
        "CHAT_ORIGIN_REQUIRED" => new("ALFA 가 이 요청을 받지 않았어요. 로그인 열쇠를 다시 등록해 주세요."),
        "INVALID_CHAT_INPUT" => new($"질문 형식이 맞지 않아요. {MaxTextLength}자 안으로 다시 적어 주세요."),
        "CHAT_ALREADY_RUNNING" => new("ALFA 가 다른 질문에 답하는 중이에요. 끝나면 다시 물어봐 주세요."),
        "ALORYS_ANSWER_INCOMPLETE" => new("ALFA 가 답을 끝까지 만들지 못했어요. 질문을 바꿔 다시 물어봐 주세요."),
        "ALORYS_UNAVAILABLE" => new("ALFA 비서가 지금 응답하지 못해요. 잠시 뒤에 다시 시도해 주세요."),
        _ when status == HttpStatusCode.Unauthorized => new("ALFA 로그인이 끝났어요. 'AI 설정'에서 다시 로그인해 주세요."),
        _ when status == HttpStatusCode.NotFound => new("ALFA 에 비서 창구가 없어요. ALFA 백엔드 버전을 확인해 주세요."),
        _ when status >= HttpStatusCode.InternalServerError => new("ALFA 백엔드가 지금 응답하지 못해요. 잠시 뒤에 다시 시도해 주세요."),
        _ => new($"ALFA 요청이 실패했어요 (HTTP {(int)status})."),
    };

    private static Exception Transport(Exception ex, CancellationToken token)
    {
        if (ex is OperationCanceledException && token.IsCancellationRequested) return ex;
        if (ex is TaskCanceledException or TimeoutException)
            return new AiException("ALFA 답이 너무 오래 걸려서 그만뒀어요. 다시 시도해 주세요.");
        if (ex is HttpRequestException)
            return new AiException("ALFA 백엔드(127.0.0.1:7701)에 연결하지 못했어요. Docker 의 alfa-backend 가 켜져 있는지 확인해 주세요.");
        return new AiException("ALFA 에 요청을 보내지 못했어요.", ex);
    }

    private static string ReadAnswer(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            var reply = node?["reply"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(reply)) throw AiFailure.EmptyAnswer("ALFA");

            // 관리 동작은 ALFA 쪽에서 승인해야 실행된다. 펫은 알리기만 한다.
            return node?["status"]?.GetValue<string>() == "approval_required"
                ? reply.Trim() + "\n\n⏳ 이 일은 ALFA 에서 승인해야 실행돼요. (펫은 대신 승인하지 않아요)"
                : reply.Trim();
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            throw new AiException("ALFA 응답을 읽지 못했어요. 잠시 뒤에 다시 시도해 주세요.", ex);
        }
    }
}
