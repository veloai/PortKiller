using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PortKiller.Core;

/// <summary>
/// ALFA 의 비서(Alorys)에게 <b>펫 전용 키</b>로 묻는다. 사용자 로그인은 쓰지 않는다.
/// 계약(ALFA 위키 <c>projects/alfa-pet-link-plan.md</c>):
/// <list type="bullet">
/// <item>연동 요청 <c>POST /api/pet-links/request</c> → 확인 코드. 주인이 ALFA 설정 › 기기 › 펫 연동에서 승인.</item>
/// <item>키 받아 가기 <c>POST /api/pet-links/{id}/pickup</c> — 승인 뒤 한 번만 키를 준다.</item>
/// <item>묻기 <c>POST /api/pet/chat</c> <c>Authorization: Bearer alfa_pet_…</c>. 키는 이 펫 입구에서만 통한다.</item>
/// </list>
///
/// <para>주소는 ALFA 허브(https, <c>uziseignk.com</c>)나 이 PC(개발용)만 받는다. 키가 다른 곳으로 새지 않게.</para>
/// <para>관리 동작이 승인을 기다리면(<c>approval_required</c>) 그 사실만 보여 주고, 펫이 대신 승인하지 않는다.</para>
/// </summary>
public sealed class AlfaChatClient : IAiClient
{
    public const string DefaultBaseUrl = "https://alfa.uziseignk.com";
    public const string KeyPrefix = "alfa_pet_";

    /// <summary>서버가 받는 질문 길이 한도(<c>PetChatController.MAX_TEXT</c> 와 같게).</summary>
    public const int MaxTextLength = 4000;

    private readonly HttpClient _http;
    private readonly Func<string?> _key;
    private readonly AiSettings _settings;

    public AlfaChatClient(HttpClient http, Func<string?> key, AiSettings settings)
    {
        _http = http;
        _key = key;
        _settings = settings;
    }

    public AiProvider Provider => AiProvider.Alfa;

    /// <summary>연동 요청 결과. 받아 가기 비밀값은 이 펫이 키를 받을 때까지만 들고 있는다.</summary>
    public sealed record LinkTicket(Guid LinkId, string PickupSecret, string ConfirmationCode, DateTimeOffset ExpiresAt);

    public enum PickupState { Pending, Issued, AlreadyIssued, Closed, NotFound }

    public sealed record PickupResult(PickupState State, string? Key);

    /// <summary>키를 보내도 되는 주소인지: ALFA 허브(https 의 uziseignk.com 계열) 또는 이 PC.</summary>
    public static bool IsAllowedBase(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;
        if (uri.IsLoopback || uri.Host == "localhost") return true;
        var host = uri.Host.ToLowerInvariant();
        return uri.Scheme == "https" && (host == "uziseignk.com" || host.EndsWith(".uziseignk.com", StringComparison.Ordinal));
    }

    /// <summary>기기 이름을 ALFA 가 받는 모양으로: 소문자·숫자·<c>.-_</c>, 64자.</summary>
    public static string NormalizeDeviceName(string? name)
    {
        var chars = (name ?? "").Trim().ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-').ToArray();
        var s = new string(chars).Trim('-', '.', '_');
        return s.Length > 64 ? s[..64] : s;
    }

    public async Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt)) throw new AiException("무엇을 물어볼지 적어 주세요.");
        prompt = prompt.Trim();
        if (prompt.Length > MaxTextLength)
            throw new AiException($"질문이 너무 길어요. {MaxTextLength}자 안으로 줄여 주세요.");

        var key = _key();
        if (string.IsNullOrWhiteSpace(key) || !key.StartsWith(KeyPrefix, StringComparison.Ordinal))
            throw new AiException("ALFA 와 아직 연동되지 않았어요. 'AI 설정'에서 [ALFA 연동 요청]을 눌러 주세요.");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl(_settings.AlfaBaseUrl)}/api/pet/chat")
        {
            Content = JsonContent.Create(new JsonObject { ["text"] = prompt }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);

        var (status, json) = await SendAsync(_http, request, cancellationToken).ConfigureAwait(false);
        if (status != HttpStatusCode.OK) throw FromError(status, ErrorCode(json));
        return ReadAnswer(json);
    }

    /// <summary>ALFA 에 연동을 요청한다. 돌려받은 확인 코드를 펫 화면에 보여 주고, ALFA 설정에서 같은 코드인지 보고 승인한다.</summary>
    public static async Task<LinkTicket> RequestLinkAsync(HttpClient http, string baseUrl, string deviceName, string petName,
                                                          string species, string platform, CancellationToken cancellationToken = default)
    {
        var device = NormalizeDeviceName(deviceName);
        if (device.Length == 0) throw new AiException("기기 이름을 적어 주세요(예: windows-office).");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl(baseUrl)}/api/pet-links/request")
        {
            Content = JsonContent.Create(new JsonObject
            {
                ["deviceName"] = device, ["petName"] = petName, ["species"] = species, ["platform"] = platform,
            }),
        };
        var (status, json) = await SendAsync(http, request, cancellationToken).ConfigureAwait(false);
        if (status == HttpStatusCode.TooManyRequests)
            throw new AiException("ALFA 에 승인 대기 요청이 너무 많아요. ALFA 설정에서 정리한 뒤 다시 요청해 주세요.");
        if (status == HttpStatusCode.BadRequest) throw new AiException("연동 요청 형식이 맞지 않아요. 기기 이름을 확인해 주세요.");
        if (status != HttpStatusCode.Created) throw FromError(status, null);
        try
        {
            var node = JsonNode.Parse(json)!;
            return new LinkTicket(
                Guid.Parse(node["linkId"]!.GetValue<string>()),
                node["pickupSecret"]!.GetValue<string>(),
                node["confirmationCode"]!.GetValue<string>(),
                DateTimeOffset.Parse(node["expiresAt"]!.GetValue<string>()));
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or NullReferenceException)
        {
            throw new AiException("ALFA 연동 응답을 읽지 못했어요.", ex);
        }
    }

    /// <summary>승인됐으면 키를 받아 온다(한 번만). 아직이면 Pending.</summary>
    public static async Task<PickupResult> PickupAsync(HttpClient http, string baseUrl, LinkTicket ticket,
                                                       CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl(baseUrl)}/api/pet-links/{ticket.LinkId}/pickup")
        {
            Content = JsonContent.Create(new JsonObject { ["pickupSecret"] = ticket.PickupSecret }),
        };
        var (status, json) = await SendAsync(http, request, cancellationToken).ConfigureAwait(false);
        switch (status)
        {
            case HttpStatusCode.Accepted: return new PickupResult(PickupState.Pending, null);
            case HttpStatusCode.Conflict: return new PickupResult(PickupState.AlreadyIssued, null);
            case HttpStatusCode.Gone: return new PickupResult(PickupState.Closed, null);
            case HttpStatusCode.NotFound: return new PickupResult(PickupState.NotFound, null);
            case HttpStatusCode.OK:
                try
                {
                    var key = JsonNode.Parse(json)?["key"]?.GetValue<string>();
                    if (key is null || !key.StartsWith(KeyPrefix, StringComparison.Ordinal))
                        throw new AiException("ALFA 가 펫 키를 돌려주지 않았어요.");
                    return new PickupResult(PickupState.Issued, key);
                }
                catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
                {
                    throw new AiException("ALFA 키 응답을 읽지 못했어요.", ex);
                }
            default: throw FromError(status, null);
        }
    }

    private static string BaseUrl(string? configured)
    {
        var url = string.IsNullOrWhiteSpace(configured) ? DefaultBaseUrl : configured.Trim();
        if (!IsAllowedBase(url))
            throw new AiException("ALFA 주소는 ALFA 허브(https://…uziseignk.com)나 이 PC 만 쓸 수 있어요. 설정을 확인해 주세요.");
        return url.TrimEnd('/');
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

    /// <summary>ALFA 가 돌려준 코드를 사람 말로. 본문을 그대로 붙이지 않는다.</summary>
    public static AiException FromError(HttpStatusCode status, string? code) => code switch
    {
        "PET_KEY_REQUIRED" => new("ALFA 연동이 끊겼어요(해제됐거나 키가 맞지 않아요). 'AI 설정'에서 다시 연동 요청해 주세요."),
        "INVALID_CHAT_INPUT" => new($"질문 형식이 맞지 않아요. {MaxTextLength}자 안으로 다시 적어 주세요."),
        "CHAT_ALREADY_RUNNING" => new("ALFA 가 앞 질문에 답하는 중이에요. 끝나면 다시 물어봐 주세요."),
        "ALORYS_ANSWER_INCOMPLETE" => new("ALFA 가 답을 끝까지 만들지 못했어요. 질문을 바꿔 다시 물어봐 주세요."),
        "ALORYS_UNAVAILABLE" => new("ALFA 비서가 지금 응답하지 못해요. 잠시 뒤에 다시 시도해 주세요."),
        _ when status == HttpStatusCode.Unauthorized => new("ALFA 연동이 끊겼어요. 'AI 설정'에서 다시 연동 요청해 주세요."),
        _ when status == HttpStatusCode.NotFound => new("ALFA 에 펫 입구가 없어요. ALFA 백엔드가 새 버전인지 확인해 주세요."),
        _ when status >= HttpStatusCode.InternalServerError => new("ALFA 가 지금 응답하지 못해요. 잠시 뒤에 다시 시도해 주세요."),
        _ => new($"ALFA 요청이 실패했어요 (HTTP {(int)status})."),
    };

    private static Exception Transport(Exception ex, CancellationToken token)
    {
        if (ex is OperationCanceledException && token.IsCancellationRequested) return ex;
        if (ex is TaskCanceledException or TimeoutException)
            return new AiException("ALFA 답이 너무 오래 걸려서 그만뒀어요. 다시 시도해 주세요.");
        if (ex is HttpRequestException)
            return new AiException("ALFA 에 연결하지 못했어요. 인터넷과 ALFA 주소를 확인해 주세요.");
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
                ? reply.Trim() + "\n\n⏳ 이 일은 ALFA(웹·텔레그램)에서 승인해야 실행돼요. 펫은 대신 승인하지 않아요."
                : reply.Trim();
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            throw new AiException("ALFA 응답을 읽지 못했어요. 잠시 뒤에 다시 시도해 주세요.", ex);
        }
    }
}
