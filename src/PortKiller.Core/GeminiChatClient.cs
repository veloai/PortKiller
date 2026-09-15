using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PortKiller.Core;

/// <summary>
/// Google Gemini 에 한 번 묻는다. 계약은 <c>v1beta/models/{model}:generateContent</c> 이고
/// 열쇠는 <c>x-goog-api-key</c> 머리글로 보낸다. 답은 <c>candidates[0].content.parts[*].text</c> 에 있다.
///
/// <para>여러 조각으로 나뉘어 오는 경우가 있어 <b>parts 를 전부 이어 붙인다</b>.
/// 첫 조각만 읽으면 답이 중간에 잘린 것처럼 보인다.</para>
/// </summary>
public sealed class GeminiChatClient : IAiClient
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com";

    private readonly HttpClient _http;
    private readonly Func<string?> _key;
    private readonly AiSettings _settings;

    /// <param name="http">
    /// 바깥에서 넣는다. 물어볼 때마다 새로 만들면 연결이 쌓여 소켓이 마르고,
    /// 검사에서는 가짜 응답기를 끼워 넣어야 하기 때문이다.
    /// </param>
    public GeminiChatClient(HttpClient http, Func<string?> key, AiSettings settings)
    {
        _http = http;
        _key = key;
        _settings = settings;
    }

    public AiProvider Provider => AiProvider.Gemini;

    public async Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt)) throw new AiException("무엇을 물어볼지 적어 주세요.");

        var key = _key();
        if (string.IsNullOrWhiteSpace(key))
            throw new AiException("Gemini 열쇠가 등록되지 않았어요. 트레이 메뉴의 'AI 설정'에서 등록해 주세요.");

        var model = _settings.ModelFor(AiProvider.Gemini);
        var body = new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray { new JsonObject { ["text"] = prompt } },
                },
            },
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = _settings.Persona } },
            },
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{BaseUrl}/v1beta/models/{Uri.EscapeDataString(model)}:generateContent")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("x-goog-api-key", key);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw AiFailure.FromTransport(ex, cancellationToken);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode) throw AiFailure.FromStatus(response.StatusCode, "Gemini");

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ReadAnswer(json);
        }
    }

    private static string ReadAnswer(string json)
    {
        try
        {
            // 배열에 [0] 을 바로 붙이지 않는다. candidates 가 빈 배열로 오는 일이 실제로 있고
            // (안전 필터에 걸린 질문 등), 그때 JsonNode 는 범위 초과로 터진다 —
            // 사용자에게는 영문 예외가 그대로 뜬다. 검사가 이걸 잡았다.
            var candidates = JsonNode.Parse(json)?["candidates"]?.AsArray();
            if (candidates is null || candidates.Count == 0) throw AiFailure.EmptyAnswer("Gemini");

            var parts = candidates[0]?["content"]?["parts"]?.AsArray();
            if (parts is null || parts.Count == 0) throw AiFailure.EmptyAnswer("Gemini");

            var text = string.Concat(parts.Select(p => p?["text"]?.GetValue<string>() ?? ""));
            if (string.IsNullOrWhiteSpace(text)) throw AiFailure.EmptyAnswer("Gemini");
            return text.Trim();
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            throw new AiException("Gemini 응답을 읽지 못했어요. 잠시 뒤에 다시 시도해 주세요.", ex);
        }
    }
}
