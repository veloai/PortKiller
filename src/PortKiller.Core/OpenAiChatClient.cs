using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PortKiller.Core;

/// <summary>
/// OpenAI 에 한 번 묻는다. <c>/v1/chat/completions</c> 에 보내고 열쇠는
/// <c>Authorization: Bearer</c> 로 붙인다. 답은 <c>choices[0].message.content</c> 다.
///
/// <para><b>온도·최대 토큰을 보내지 않는다.</b> 모델 세대마다 받는 칸 이름과 허용값이 달라서
/// (max_tokens / max_completion_tokens, 고정 temperature) 보내는 순간 모델을 바꿀 때마다
/// 400 이 난다. 우리에게 필요한 값도 아니다 — 길이는 말투 지시로 줄인다.</para>
/// </summary>
public sealed class OpenAiChatClient : IAiClient
{
    private const string Url = "https://api.openai.com/v1/chat/completions";

    private readonly HttpClient _http;
    private readonly Func<string?> _key;
    private readonly AiSettings _settings;

    public OpenAiChatClient(HttpClient http, Func<string?> key, AiSettings settings)
    {
        _http = http;
        _key = key;
        _settings = settings;
    }

    public AiProvider Provider => AiProvider.OpenAi;

    public async Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt)) throw new AiException("무엇을 물어볼지 적어 주세요.");

        var key = _key();
        if (string.IsNullOrWhiteSpace(key))
            throw new AiException("OpenAI 열쇠가 등록되지 않았어요. 트레이 메뉴의 'AI 설정'에서 등록해 주세요.");

        var body = new JsonObject
        {
            ["model"] = _settings.ModelFor(AiProvider.OpenAi),
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = _settings.Persona },
                new JsonObject { ["role"] = "user", ["content"] = prompt },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Url)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

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
            if (!response.IsSuccessStatusCode) throw AiFailure.FromStatus(response.StatusCode, "OpenAI");

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ReadAnswer(json);
        }
    }

    private static string ReadAnswer(string json)
    {
        try
        {
            // 배열에 [0] 을 바로 붙이지 않는다 — choices 가 빈 배열로 오면 JsonNode 가
            // 범위 초과로 터지고, 사용자는 영문 예외를 보게 된다(Gemini 쪽에서 실제로 났다).
            var choices = JsonNode.Parse(json)?["choices"]?.AsArray();
            if (choices is null || choices.Count == 0) throw AiFailure.EmptyAnswer("OpenAI");

            var text = choices[0]?["message"]?["content"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(text)) throw AiFailure.EmptyAnswer("OpenAI");
            return text.Trim();
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            throw new AiException("OpenAI 응답을 읽지 못했어요. 잠시 뒤에 다시 시도해 주세요.", ex);
        }
    }
}
