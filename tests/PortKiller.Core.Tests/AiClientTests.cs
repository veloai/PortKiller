using System.Net;
using System.Text;
using PortKiller.Core;
using Xunit;

namespace PortKiller.Core.Tests;

/// <summary>
/// AI 클라이언트 검사. <b>진짜 호출은 한 번도 하지 않는다</b> — 돈이 나가고, 네트워크가 없는
/// 자리에서 검사가 깨지며, 열쇠가 있어야 돌아가는 검사는 아무도 못 돌린다.
/// 가짜 응답기를 끼워 넣고 "이런 응답이 오면 사용자에게 무엇이 보이는가"만 본다.
///
/// <para>여기서 막는 것은 전부 <b>사용자가 실제로 겪을 실패</b>다 — 열쇠 미등록, 열쇠 거절,
/// 모델 이름 오타, 한도 초과, 네트워크 끊김, 응답이 깨져서 옴.</para>
/// </summary>
public class AiClientTests
{
    /// <summary>미리 정해 둔 답만 돌려주는 가짜 응답기. 보낸 요청도 들고 있어서 내용을 확인할 수 있다.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _reply;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) => _reply = reply;

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return _reply(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (GeminiChatClient client, StubHandler handler) Gemini(
        Func<HttpRequestMessage, HttpResponseMessage> reply, string? key = "test-key-not-real")
    {
        var handler = new StubHandler(reply);
        return (new GeminiChatClient(new HttpClient(handler), () => key, new AiSettings()), handler);
    }

    private static (OpenAiChatClient client, StubHandler handler) OpenAi(
        Func<HttpRequestMessage, HttpResponseMessage> reply, string? key = "test-key-not-real")
    {
        var handler = new StubHandler(reply);
        return (new OpenAiChatClient(new HttpClient(handler), () => key, new AiSettings()), handler);
    }

    // ---------- 잘 되는 경우 ----------

    [Fact]
    public async Task Gemini_reads_the_answer_out_of_the_response()
    {
        var (client, _) = Gemini(_ => Json(HttpStatusCode.OK, """
            {"candidates":[{"content":{"parts":[{"text":"안녕! 무슨 일이야?"}]}}]}
            """));

        Assert.Equal("안녕! 무슨 일이야?", await client.AskAsync("안녕"));
    }

    /// <summary>답이 여러 조각으로 쪼개져 오는 경우가 있다. 첫 조각만 읽으면 문장이 잘린다.</summary>
    [Fact]
    public async Task Gemini_joins_every_part_not_just_the_first()
    {
        var (client, _) = Gemini(_ => Json(HttpStatusCode.OK, """
            {"candidates":[{"content":{"parts":[{"text":"앞부분 "},{"text":"뒷부분"}]}}]}
            """));

        Assert.Equal("앞부분 뒷부분", await client.AskAsync("안녕"));
    }

    [Fact]
    public async Task OpenAi_reads_the_answer_out_of_the_response()
    {
        var (client, _) = OpenAi(_ => Json(HttpStatusCode.OK, """
            {"choices":[{"message":{"role":"assistant","content":"그건 이래."}}]}
            """));

        Assert.Equal("그건 이래.", await client.AskAsync("왜?"));
    }

    [Fact]
    public async Task Gemini_sends_the_key_as_a_header_and_the_prompt_in_the_body()
    {
        var (client, handler) = Gemini(_ => Json(HttpStatusCode.OK, """
            {"candidates":[{"content":{"parts":[{"text":"응"}]}}]}
            """));

        await client.AskAsync("점심 뭐 먹지");

        Assert.True(handler.LastRequest!.Headers.Contains("x-goog-api-key"));
        Assert.Contains(AiSettings.DefaultGeminiModel, handler.LastRequest.RequestUri!.ToString());

        // 본문을 글자로 훑지 않고 다시 읽어서 확인한다. System.Text.Json 은 한글을 \uXXXX 로
        // 바꿔 내보내므로(정상 동작이다) 원문 조각 찾기는 늘 실패한다.
        // 중요한 것은 "질문이 맞는 칸에 들어갔는가"이지 바이트 모양이 아니다.
        var sent = System.Text.Json.Nodes.JsonNode.Parse(handler.LastBody!);
        Assert.Equal("점심 뭐 먹지",
            sent!["contents"]![0]!["parts"]![0]!["text"]!.GetValue<string>());
        Assert.Equal(AiSettings.DefaultPersona,
            sent["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>());
    }

    /// <summary>
    /// 모델 세대마다 받는 칸이 달라서, 온도와 최대 토큰을 보내면 모델을 바꿀 때마다 400 이 난다.
    /// 그래서 아예 안 보낸다 — 실수로 되살아나지 않게 못 박는다.
    /// </summary>
    [Fact]
    public async Task OpenAi_does_not_send_temperature_or_token_limits()
    {
        var (client, handler) = OpenAi(_ => Json(HttpStatusCode.OK, """
            {"choices":[{"message":{"content":"응"}}]}
            """));

        await client.AskAsync("안녕");

        Assert.DoesNotContain("temperature", handler.LastBody);
        Assert.DoesNotContain("max_tokens", handler.LastBody);
        Assert.DoesNotContain("max_completion_tokens", handler.LastBody);
    }

    // ---------- 실패한 경우 ----------

    [Fact]
    public async Task No_key_means_a_message_that_says_where_to_register_it()
    {
        var (client, _) = Gemini(_ => throw new InvalidOperationException("호출되면 안 된다"), key: null);

        var ex = await Assert.ThrowsAsync<AiException>(() => client.AskAsync("안녕"));
        Assert.Contains("등록", ex.Message);
    }

    [Fact]
    public async Task Empty_prompt_is_refused_before_any_request_goes_out()
    {
        var (client, handler) = Gemini(_ => throw new InvalidOperationException("호출되면 안 된다"));

        await Assert.ThrowsAsync<AiException>(() => client.AskAsync("   "));
        Assert.Null(handler.LastRequest);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "열쇠")]
    [InlineData(HttpStatusCode.Forbidden, "열쇠")]
    [InlineData(HttpStatusCode.NotFound, "모델")]
    [InlineData(HttpStatusCode.TooManyRequests, "잦")]
    [InlineData(HttpStatusCode.InternalServerError, "서버")]
    public async Task Http_failures_turn_into_something_a_person_can_act_on(
        HttpStatusCode status, string expectedWord)
    {
        var (client, _) = Gemini(_ => Json(status, """{"error":{"message":"whatever"}}"""));

        var ex = await Assert.ThrowsAsync<AiException>(() => client.AskAsync("안녕"));
        Assert.Contains(expectedWord, ex.Message);
    }

    /// <summary>
    /// 401 응답 본문에 보낸 열쇠를 되비쳐 주는 서비스가 있다. 그 본문을 화면에 붙이면
    /// 열쇠가 화면과 로그에 남는다. 그래서 본문은 쓰지 않는다.
    /// </summary>
    [Fact]
    public async Task Error_message_never_carries_the_response_body_back_to_the_screen()
    {
        var (client, _) = Gemini(_ => Json(HttpStatusCode.Unauthorized, """
            {"error":{"message":"API key not valid: test-key-not-real"}}
            """));

        var ex = await Assert.ThrowsAsync<AiException>(() => client.AskAsync("안녕"));
        Assert.DoesNotContain("test-key-not-real", ex.Message);
    }

    [Fact]
    public async Task No_network_says_so_instead_of_showing_an_english_exception()
    {
        var (client, _) = Gemini(_ => throw new HttpRequestException("No such host is known."));

        var ex = await Assert.ThrowsAsync<AiException>(() => client.AskAsync("안녕"));
        Assert.Contains("인터넷", ex.Message);
    }

    [Fact]
    public async Task A_timeout_reads_as_a_timeout_not_as_a_crash()
    {
        var (client, _) = Gemini(_ => throw new TaskCanceledException("timed out"));

        var ex = await Assert.ThrowsAsync<AiException>(() => client.AskAsync("안녕"));
        Assert.Contains("오래", ex.Message);
    }

    /// <summary>사용자가 직접 끈 것은 오류가 아니다. 그걸 실패로 알리면 화면이 거짓말을 한다.</summary>
    [Fact]
    public async Task Cancelling_on_purpose_is_not_reported_as_a_failure()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var (client, _) = Gemini(_ => throw new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.AskAsync("안녕", cts.Token));
    }

    [Fact]
    public async Task A_response_with_no_answer_in_it_is_reported_plainly()
    {
        var (client, _) = Gemini(_ => Json(HttpStatusCode.OK, """{"candidates":[]}"""));

        var ex = await Assert.ThrowsAsync<AiException>(() => client.AskAsync("안녕"));
        Assert.Contains("답", ex.Message);
    }

    [Fact]
    public async Task Broken_json_does_not_escape_as_a_raw_parser_error()
    {
        var (client, _) = OpenAi(_ => Json(HttpStatusCode.OK, "{ this is not json"));

        var ex = await Assert.ThrowsAsync<AiException>(() => client.AskAsync("안녕"));
        Assert.Contains("읽지 못했", ex.Message);
    }

    // ---------- 설정 ----------

    [Fact]
    public void A_blank_model_name_falls_back_to_the_default_instead_of_sending_nothing()
    {
        var settings = new AiSettings { GeminiModel = "  ", OpenAiModel = "" };

        Assert.Equal(AiSettings.DefaultGeminiModel, settings.ModelFor(AiProvider.Gemini));
        Assert.Equal(AiSettings.DefaultOpenAiModel, settings.ModelFor(AiProvider.OpenAi));
    }

    [Fact]
    public void A_model_name_typed_with_stray_spaces_still_works()
    {
        var settings = new AiSettings { GeminiModel = "  gemini-3-flash  " };

        Assert.Equal("gemini-3-flash", settings.ModelFor(AiProvider.Gemini));
    }
}
