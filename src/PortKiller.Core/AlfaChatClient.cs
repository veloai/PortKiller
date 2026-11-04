using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PortKiller.Core;

/// <summary>
/// ALFA 의 비서(Alorys)에게 <b>펫 전용 키</b>로 묻는다. 사용자 로그인은 쓰지 않는다.
/// 계약(rev.3, ALFA 위키 <c>projects/alfa-pet-link-plan.md</c> §8):
/// <list type="bullet">
/// <item>A1 연동 요청 <c>POST /api/pet-links/request</c> → 확인 코드. 주인이 ALFA 설정 › 기기에서 승인.</item>
/// <item>A2 키 받아 가기 <c>POST /api/pet-links/{id}/pickup</c> — 승인 뒤 한 번만 키를 준다.</item>
/// <item>A6 묻기 <c>POST /api/pet/chat</c> <c>Authorization: Bearer alfa_pet_…</c>.</item>
/// <item>A7 연결 확인 <c>GET /api/pet/me</c>, A8 연동 끊기 <c>POST /api/pet/unlink</c>.</item>
/// </list>
///
/// <para>주소는 ALFA 허브(https, <c>uziseignk.com</c>)나 이 PC(개발용)만 받는다. 키가 다른 곳으로 새지 않게.</para>
/// <para>오류는 예외가 아니라 결과 값으로 돌려준다(묻기만 예외 — <see cref="IAiClient"/> 계약).
/// 받아 가기는 네트워크가 끊겨도 예외를 던지지 않는다: 펫 본체가 서버가 닫힘을 알릴 때까지 계속 묻는다(C1).</para>
/// </summary>
public sealed partial class AlfaChatClient : IAiClient
{
    public const string DefaultBaseUrl = "https://alfa.uziseignk.com";
    public const string KeyPrefix = "alfa_pet_";

    /// <summary>ALFA 가 쓰는 이 PC 의 기본 이름(위키·기기 설정 <c>alfa-device.env</c>).</summary>
    public const string DefaultDeviceName = "windows-office";

    /// <summary>서버가 받는 질문 길이 한도.</summary>
    public const int MaxTextLength = 4000;

    /// <summary>기기 이름 최대 길이. 대화방 이름 <c>pet:&lt;deviceName&gt;</c> 이 ALFA inbox chat_id(64자)에 들어가야 한다.</summary>
    public const int MaxDeviceNameLength = 60;

    /// <summary>펫 방이 거절한 일(알림·할 일 삭제)을 받았을 때 덧붙이는 말.</summary>
    public const string UnsupportedNote = "🐾 알림과 할 일 삭제는 펫에서 할 수 없어요. ALFA 웹이나 텔레그램에서 해 주세요.";

    public const string ConfirmNote = "⏳ 이 일은 ALFA(웹·텔레그램)에서 승인해야 실행돼요. 펫은 대신 승인하지 않아요.";

    private readonly HttpClient _http;
    private readonly Func<string?> _key;
    private readonly AiSettings _settings;
    private readonly Action? _unauthorized;

    /// <param name="unauthorized">묻기가 401 을 받았을 때(키가 해제됨). 연동 상태를 "끊김"으로 바꾸라고 알린다(C9).</param>
    public AlfaChatClient(HttpClient http, Func<string?> key, AiSettings settings, Action? unauthorized = null)
    {
        _http = http;
        _key = key;
        _settings = settings;
        _unauthorized = unauthorized;
    }

    public AiProvider Provider => AiProvider.Alfa;

    // ---------- 결과 모양 ----------

    /// <summary>연동 요청 결과. 받아 가기 비밀값은 메모리에만 둔다. 글자로 바꿔도 비밀값은 나오지 않는다.</summary>
    public sealed record LinkTicket(Guid LinkId, string PickupSecret, string ConfirmationCode, DateTimeOffset ExpiresAt)
    {
        private bool PrintMembers(System.Text.StringBuilder builder)
        {
            builder.Append($"LinkId = {LinkId}, ConfirmationCode = {ConfirmationCode}, ExpiresAt = {ExpiresAt:O}");
            return true;
        }
    }

    public enum RequestOutcome { Created, Invalid, Limit, RateLimited, Conflict, Unreachable, Failed }

    public sealed record RequestResult(RequestOutcome Outcome, LinkTicket? Ticket)
    {
        /// <summary>사용자에게 보일 한 줄. 만들어졌으면 null.</summary>
        public string? Message => Outcome switch
        {
            RequestOutcome.Created => null,
            RequestOutcome.Invalid => "연동 요청 형식이 맞지 않아요. 기기 이름을 확인해 주세요.",
            RequestOutcome.Limit => "ALFA 에 승인 대기 요청이 너무 많아요. ALFA 설정에서 정리하거나 10분 뒤 다시 요청해 주세요.",
            RequestOutcome.RateLimited => "요청을 너무 자주 보냈어요. 10분쯤 뒤에 다시 요청해 주세요.",
            RequestOutcome.Conflict => "다른 요청과 겹쳤어요. 잠시 뒤 다시 요청해 주세요.",
            RequestOutcome.Unreachable => "ALFA 에 연결하지 못했어요. 인터넷과 ALFA 주소를 확인해 주세요.",
            _ => "ALFA 에 연동을 요청하지 못했어요.",
        };
    }

    /// <summary>
    /// 받아 가기 결과. <see cref="Retry"/> 는 네트워크 오류·서버 오류처럼 "다시 물어보면 되는" 것이다.
    /// 끝(<see cref="IsFinal"/>)은 서버가 200/409/410/404 를 줄 때뿐이다.
    /// </summary>
    public enum PickupState { Pending, Retry, Issued, AlreadyIssued, Closed, NotFound }

    /// <summary>키는 저장할 때만 꺼내 쓴다. 글자로 바꿔도 키는 나오지 않는다.</summary>
    public sealed record PickupResult(PickupState State, string? Key)
    {
        public bool IsFinal => State is PickupState.Issued or PickupState.AlreadyIssued or PickupState.Closed or PickupState.NotFound;

        private bool PrintMembers(System.Text.StringBuilder builder)
        {
            builder.Append($"State = {State}, HasKey = {Key is not null}");
            return true;
        }
    }

    public enum LinkState { Connected, Unlinked, Unreachable }

    /// <summary>A7 결과. 연결됐으면 ALFA 가 아는 기기 이름도 함께.</summary>
    public sealed record MeResult(LinkState State, string? DeviceName = null);

    public enum UnlinkOutcome { Revoked, AlreadyUnlinked, Unreachable, Failed }

    /// <summary>A6 답. <see cref="Status"/> 는 completed / unsupported / needs-confirmation.</summary>
    public sealed record ChatReply(string Reply, string? Source, string Status);

    // ---------- 주소·이름 규칙 ----------

    /// <summary>키를 보내도 되는 주소인지: ALFA 허브(https 의 uziseignk.com 계열) 또는 이 PC.</summary>
    public static bool IsAllowedBase(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;
        if (uri.IsLoopback || uri.Host == "localhost") return true;
        var host = uri.Host.ToLowerInvariant();
        return uri.Scheme == "https" && (host == "uziseignk.com" || host.EndsWith(".uziseignk.com", StringComparison.Ordinal));
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,59}$")]
    private static partial Regex DeviceNameRule();

    /// <summary>ALFA 가 받는 기기 이름인지(<c>^[a-z0-9][a-z0-9._-]{0,59}$</c>).</summary>
    public static bool IsValidDeviceName(string? name) => name is not null && DeviceNameRule().IsMatch(name);

    /// <summary>기기 이름을 ALFA 가 받는 모양으로: 소문자·숫자·<c>.-_</c>, 첫 글자는 영문·숫자, 60자.</summary>
    public static string NormalizeDeviceName(string? name)
    {
        var chars = (name ?? "").Trim().ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-').ToArray();
        var s = new string(chars).Trim('-', '.', '_');
        if (s.Length > MaxDeviceNameLength) s = s[..MaxDeviceNameLength].TrimEnd('-', '.', '_');
        return s;
    }

    // ---------- A6 묻기 ----------

    public async Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var reply = await ChatAsync(prompt, cancellationToken).ConfigureAwait(false);
        return reply.Status switch
        {
            "unsupported" => reply.Reply + "\n\n" + UnsupportedNote,
            "needs-confirmation" or "approval_required" => reply.Reply + "\n\n" + ConfirmNote,
            _ => reply.Reply,
        };
    }

    public async Task<ChatReply> ChatAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt)) throw new AiException("무엇을 물어볼지 적어 주세요.");
        prompt = prompt.Trim();
        if (prompt.Length > MaxTextLength)
            throw new AiException($"질문이 너무 길어요. {MaxTextLength}자 안으로 줄여 주세요.");

        var key = _key();
        if (!LooksLikeKey(key))
            throw new AiException("ALFA 와 아직 연동되지 않았어요. [ALFA 연동하기]를 눌러 주세요.");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl(_settings.AlfaBaseUrl)}/api/pet/chat")
        {
            Content = JsonContent.Create(new JsonObject { ["text"] = prompt }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);

        var (status, json) = await SendAsync(_http, request, cancellationToken).ConfigureAwait(false);
        if (status == HttpStatusCode.Unauthorized) _unauthorized?.Invoke();
        if (status != HttpStatusCode.OK) throw FromError(status, ErrorCode(json));
        return ReadReply(json);
    }

    /// <summary>ALFA 가 돌려준 코드를 사람 말로. 본문을 그대로 붙이지 않는다.</summary>
    public static AiException FromError(HttpStatusCode status, string? code) => code switch
    {
        "PET_KEY_REQUIRED" => new("ALFA 연결이 끊겼어요(해제됐거나 키가 맞지 않아요). [ALFA 연동하기]로 다시 연동해 주세요."),
        "TEXT_REQUIRED" => new($"질문을 적어 주세요. {MaxTextLength}자 안으로 적으면 돼요."),
        "PET_BUSY" => new("ALFA 가 앞 질문에 답하는 중이에요. 끝나면 다시 물어봐 주세요."),
        "ALORYS_UNAVAILABLE" => new("ALFA 비서가 지금 응답하지 못해요. 잠시 뒤에 다시 시도해 주세요."),
        _ when status == HttpStatusCode.Unauthorized => new("ALFA 연결이 끊겼어요. [ALFA 연동하기]로 다시 연동해 주세요."),
        _ when status == HttpStatusCode.NotFound => new("ALFA 에 펫 입구가 없어요. ALFA 백엔드가 새 버전인지 확인해 주세요."),
        _ when status >= HttpStatusCode.InternalServerError => new("ALFA 가 지금 응답하지 못해요. 잠시 뒤에 다시 시도해 주세요."),
        _ => new($"ALFA 요청이 실패했어요 (HTTP {(int)status})."),
    };

    // ---------- A1 연동 요청 ----------

    /// <summary>ALFA 에 연동을 요청한다. 돌려받은 확인 코드를 펫 화면에 보여 주고, ALFA 설정에서 같은 코드인지 보고 승인한다.</summary>
    public static async Task<RequestResult> RequestLinkAsync(HttpClient http, string baseUrl, string deviceName, string petName,
                                                             string species, string platform, CancellationToken cancellationToken = default)
    {
        var device = NormalizeDeviceName(deviceName);
        if (!IsValidDeviceName(device) || !TryBaseUrl(baseUrl, out var url)) return new(RequestOutcome.Invalid, null);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{url}/api/pet-links/request")
        {
            Content = JsonContent.Create(new JsonObject
            {
                ["deviceName"] = device, ["petName"] = petName, ["species"] = species, ["platform"] = platform,
            }),
        };
        HttpStatusCode status;
        string json;
        try
        {
            (status, json) = await SendAsync(http, request, cancellationToken).ConfigureAwait(false);
        }
        catch (AiException)
        {
            return new(RequestOutcome.Unreachable, null);
        }

        switch (status)
        {
            case HttpStatusCode.Created:
                try
                {
                    var node = JsonNode.Parse(json)!;
                    return new(RequestOutcome.Created, new LinkTicket(
                        Guid.Parse(node["linkId"]!.GetValue<string>()),
                        node["pickupSecret"]!.GetValue<string>(),
                        node["confirmationCode"]!.GetValue<string>(),
                        DateTimeOffset.Parse(node["expiresAt"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture)));
                }
                catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or NullReferenceException)
                {
                    return new(RequestOutcome.Failed, null);
                }
            case HttpStatusCode.BadRequest: return new(RequestOutcome.Invalid, null);
            // 서버가 같은 429 로 두 가지를 알린다: 대기 수 한도(PET_LINK_LIMIT)와 같은 곳 횟수 제한(PET_LINK_RATE_LIMIT).
            case HttpStatusCode.TooManyRequests:
                return new(json.Contains("PET_LINK_RATE_LIMIT", StringComparison.Ordinal) ? RequestOutcome.RateLimited : RequestOutcome.Limit, null);
            case HttpStatusCode.Conflict: return new(RequestOutcome.Conflict, null);
            default:
                return new(status >= HttpStatusCode.InternalServerError ? RequestOutcome.Unreachable : RequestOutcome.Failed, null);
        }
    }

    // ---------- A2 키 받아 가기 ----------

    /// <summary>승인됐으면 키를 받아 온다(한 번만). 아직이면 Pending, 네트워크·서버 오류면 Retry — 예외를 던지지 않는다.</summary>
    public static async Task<PickupResult> PickupAsync(HttpClient http, string baseUrl, LinkTicket ticket,
                                                       CancellationToken cancellationToken = default)
    {
        if (!TryBaseUrl(baseUrl, out var url)) return new(PickupState.Retry, null);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{url}/api/pet-links/{ticket.LinkId}/pickup")
        {
            Content = JsonContent.Create(new JsonObject { ["pickupSecret"] = ticket.PickupSecret }),
        };
        HttpStatusCode status;
        string json;
        try
        {
            (status, json) = await SendAsync(http, request, cancellationToken).ConfigureAwait(false);
        }
        catch (AiException)
        {
            return new(PickupState.Retry, null);
        }

        switch (status)
        {
            case HttpStatusCode.Accepted: return new(PickupState.Pending, null);
            case HttpStatusCode.Conflict: return new(PickupState.AlreadyIssued, null);
            case HttpStatusCode.Gone: return new(PickupState.Closed, null);
            case HttpStatusCode.NotFound: return new(PickupState.NotFound, null);
            case HttpStatusCode.OK:
                try
                {
                    var key = JsonNode.Parse(json)?["key"]?.GetValue<string>();
                    // 키가 이상하면 받은 것으로 치지 않고 다시 묻는다. 서버는 한 번만 주므로 다음엔 409 가 온다.
                    return LooksLikeKey(key) ? new(PickupState.Issued, key) : new(PickupState.Retry, null);
                }
                catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
                {
                    return new(PickupState.Retry, null);
                }
            default: return new(PickupState.Retry, null);
        }
    }

    /// <summary>
    /// 서버가 끝을 알릴 때까지(200/409/410/404) <paramref name="interval"/> 마다 받아 가기를 되풀이한다(W3, C1).
    /// 만료 시각이 지나도 스스로 멈추지 않는다 — 늦게 승인돼도 키를 받아 가야 한다. 멈추는 것은 취소뿐이다.
    /// </summary>
    public static async Task<PickupResult> PollPickupAsync(HttpClient http, string baseUrl, LinkTicket ticket, TimeSpan interval,
                                                           Func<TimeSpan, CancellationToken, Task>? delay = null,
                                                           TimeSpan? attemptTimeout = null,
                                                           Action<PickupResult>? onAttempt = null,
                                                           CancellationToken cancellationToken = default)
    {
        delay ??= Task.Delay;
        while (true)
        {
            await delay(interval, cancellationToken).ConfigureAwait(false);
            PickupResult result;
            using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                if (attemptTimeout is { } t) attempt.CancelAfter(t);
                try
                {
                    result = await PickupAsync(http, baseUrl, ticket, attempt.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    result = new(PickupState.Retry, null);   // 한 번 늦었다. 다음 차례에 다시.
                }
            }
            onAttempt?.Invoke(result);
            if (result.IsFinal) return result;
        }
    }

    // ---------- A7 연결 확인 ----------

    /// <summary>
    /// 연동이 살아 있는지 확인하고, ALFA 설정 목록의 "마지막 접속"을 남긴다(<c>GET /api/pet/me</c>).
    /// 401 은 해제됐거나 키가 맞지 않는 것, 그 밖의 실패는 연결 불가로 본다. 키를 지우지는 않는다.
    /// </summary>
    public static async Task<MeResult> MeAsync(HttpClient http, string baseUrl, string? key, CancellationToken cancellationToken = default)
    {
        if (!LooksLikeKey(key)) return new(LinkState.Unlinked);
        if (!TryBaseUrl(baseUrl, out var url)) return new(LinkState.Unreachable);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{url}/api/pet/me");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        try
        {
            var (status, json) = await SendAsync(http, request, cancellationToken).ConfigureAwait(false);
            if (status == HttpStatusCode.Unauthorized) return new(LinkState.Unlinked);
            if (status != HttpStatusCode.OK) return new(LinkState.Unreachable);
            string? device = null;
            try { device = JsonNode.Parse(json)?["deviceName"]?.GetValue<string>(); }
            catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException) { }
            return new(LinkState.Connected, device);
        }
        catch (AiException)
        {
            return new(LinkState.Unreachable);
        }
    }

    // ---------- A8 연동 끊기 ----------

    /// <summary>이 펫 쪽에서 연동을 끊는다. 401 도 "이미 끊김"이라 둘 다 키를 지워도 된다.</summary>
    public static async Task<UnlinkOutcome> UnlinkAsync(HttpClient http, string baseUrl, string? key, CancellationToken cancellationToken = default)
    {
        if (!LooksLikeKey(key)) return UnlinkOutcome.AlreadyUnlinked;
        if (!TryBaseUrl(baseUrl, out var url)) return UnlinkOutcome.Failed;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{url}/api/pet/unlink");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        try
        {
            var (status, _) = await SendAsync(http, request, cancellationToken).ConfigureAwait(false);
            return status switch
            {
                HttpStatusCode.OK => UnlinkOutcome.Revoked,
                HttpStatusCode.Unauthorized => UnlinkOutcome.AlreadyUnlinked,
                >= HttpStatusCode.InternalServerError => UnlinkOutcome.Unreachable,
                _ => UnlinkOutcome.Failed,
            };
        }
        catch (AiException)
        {
            return UnlinkOutcome.Unreachable;
        }
    }

    // ---------- 공통 ----------

    public static bool LooksLikeKey(string? key)
        => !string.IsNullOrWhiteSpace(key) && key.StartsWith(KeyPrefix, StringComparison.Ordinal);

    private static bool TryBaseUrl(string? configured, out string url)
    {
        url = string.IsNullOrWhiteSpace(configured) ? DefaultBaseUrl : configured.Trim();
        if (!IsAllowedBase(url)) return false;
        url = url.TrimEnd('/');
        return true;
    }

    private static string BaseUrl(string? configured)
    {
        if (!TryBaseUrl(configured, out var url))
            throw new AiException("ALFA 주소는 ALFA 허브(https://…uziseignk.com)나 이 PC 만 쓸 수 있어요. 설정을 확인해 주세요.");
        return url;
    }

    private static async Task<(HttpStatusCode, string)> SendAsync(HttpClient http, HttpRequestMessage request, CancellationToken cancellationToken)
    {
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
            return (response.StatusCode, json);
        }
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

    private static Exception Transport(Exception ex, CancellationToken token)
    {
        if (ex is OperationCanceledException && token.IsCancellationRequested) return ex;
        if (ex is TaskCanceledException or TimeoutException)
            return new AiException("ALFA 답이 너무 오래 걸려서 그만뒀어요. 다시 시도해 주세요.");
        if (ex is HttpRequestException)
            return new AiException("ALFA 에 연결하지 못했어요. 인터넷과 ALFA 주소를 확인해 주세요.");
        return new AiException("ALFA 에 요청을 보내지 못했어요.", ex);
    }

    private static ChatReply ReadReply(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            var reply = node?["reply"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(reply)) throw AiFailure.EmptyAnswer("ALFA");
            return new ChatReply(reply.Trim(), node?["source"]?.GetValue<string>(), node?["status"]?.GetValue<string>() ?? "completed");
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            throw new AiException("ALFA 응답을 읽지 못했어요. 잠시 뒤에 다시 시도해 주세요.", ex);
        }
    }
}
