using System.Net;
using System.Text;
using PortKiller.Core;
using Xunit;

namespace PortKiller.Core.Tests;

/// <summary>
/// ALFA 비서 클라이언트 검사. 진짜 ALFA 는 부르지 않고 가짜 응답기로 "이런 답이 오면 무엇이 보이나"만 본다.
/// </summary>
public class AlfaChatClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _reply;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) => _reply = reply;

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return _reply(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (AlfaChatClient client, StubHandler handler) Alfa(
        Func<HttpRequestMessage, HttpResponseMessage> reply, string? token = "test-token-not-real", AiSettings? settings = null)
    {
        var handler = new StubHandler(reply);
        return (new AlfaChatClient(new HttpClient(handler), () => token, settings ?? new AiSettings()), handler);
    }

    [Fact]
    public async Task Asks_the_local_alorys_chat_with_bearer_and_reads_the_reply()
    {
        var (client, handler) = Alfa(_ => Json(HttpStatusCode.OK, """{"reply":"alfa-backend 는 정상이에요.","source":"alorys-ai","status":"completed"}"""));

        var answer = await client.AskAsync("  alfa-backend 상태 봐 줘 ");

        Assert.Equal("alfa-backend 는 정상이에요.", answer);
        Assert.Equal("http://127.0.0.1:7701/api/alorys/chat", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("test-token-not-real", handler.LastRequest.Headers.Authorization.Parameter);
        var body = System.Text.Json.Nodes.JsonNode.Parse(handler.LastBody!)!;
        Assert.Equal("alfa-backend 상태 봐 줘", body["text"]!.GetValue<string>());
        Assert.Empty(body["history"]!.AsArray());
    }

    [Fact]
    public async Task Approval_required_is_shown_and_never_confirmed_by_the_pet()
    {
        var (client, handler) = Alfa(_ => Json(HttpStatusCode.OK, """{"reply":"재시작할까요?","source":"alorys","status":"approval_required"}"""));

        var answer = await client.AskAsync("hermes 재시작해");

        Assert.StartsWith("재시작할까요?", answer);
        Assert.Contains("ALFA 에서 승인", answer);
        Assert.Equal(1, handler.Calls);   // 승인 요청을 따로 보내지 않는다
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED", "다시 로그인")]
    [InlineData(HttpStatusCode.Forbidden, "AUTHORIZATION_REQUIRED", "주인 계정")]
    [InlineData(HttpStatusCode.TooManyRequests, "CHAT_ALREADY_RUNNING", "다른 질문")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ALORYS_UNAVAILABLE", "응답하지 못해요")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ALORYS_ANSWER_INCOMPLETE", "끝까지")]
    public async Task Server_error_codes_become_plain_words(HttpStatusCode status, string code, string expected)
    {
        var (client, _) = Alfa(_ => Json(status, $$"""{"error":"{{code}}"}"""));

        var ex = await Assert.ThrowsAsync<AiException>(() => client.AskAsync("안녕"));

        Assert.Contains(expected, ex.Message);
        Assert.DoesNotContain(code, ex.Message);
    }

    [Fact]
    public async Task No_token_means_no_request()
    {
        var (client, handler) = Alfa(_ => Json(HttpStatusCode.OK, "{}"), token: null);

        var ex = await Assert.ThrowsAsync<AiException>(() => client.AskAsync("안녕"));

        Assert.Contains("로그인", ex.Message);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Too_long_question_is_stopped_before_sending()
    {
        var (client, handler) = Alfa(_ => Json(HttpStatusCode.OK, "{}"));

        await Assert.ThrowsAsync<AiException>(() => client.AskAsync(new string('가', AlfaChatClient.MaxTextLength + 1)));

        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("https://alfa.uziseignk.com")]
    [InlineData("http://192.168.0.10:7701")]
    [InlineData("not a url")]
    public async Task The_token_is_never_sent_off_this_pc(string url)
    {
        var (client, handler) = Alfa(_ => Json(HttpStatusCode.OK, "{}"), settings: new AiSettings { AlfaBaseUrl = url });

        await Assert.ThrowsAsync<AiException>(() => client.AskAsync("안녕"));

        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("http://127.0.0.1:7701", true)]
    [InlineData("http://localhost:7701/", true)]
    [InlineData("http://[::1]:7701", true)]
    [InlineData("https://alfa.uziseignk.com", false)]
    [InlineData("file:///c:/x", false)]
    public void Only_loopback_addresses_count_as_local(string url, bool expected)
        => Assert.Equal(expected, AlfaChatClient.IsLocal(url));

    [Fact]
    public async Task Login_returns_only_the_token()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"userId":"u","username":"me","displayName":"나","grade":"master","token":"session-token","lastLoginAt":null}"""));

        var token = await AlfaChatClient.LoginAsync(new HttpClient(handler), AlfaChatClient.DefaultBaseUrl, " me ", "pw");

        Assert.Equal("session-token", token);
        Assert.Equal("http://127.0.0.1:7701/api/auth/login", handler.LastRequest!.RequestUri!.ToString());
        Assert.Contains("\"username\":\"me\"", handler.LastBody);
    }

    [Fact]
    public async Task Login_failure_does_not_echo_the_server_body()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Unauthorized, """{"error":"invalid credentials for me"}"""));

        var ex = await Assert.ThrowsAsync<AiException>(() =>
            AlfaChatClient.LoginAsync(new HttpClient(handler), AlfaChatClient.DefaultBaseUrl, "me", "wrong"));

        Assert.Equal("아이디나 비밀번호가 맞지 않아요.", ex.Message);
    }

    [Fact]
    public async Task Passkey_only_account_is_told_to_paste_a_token()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Unauthorized, """{"error":"passkey login required"}"""));

        var ex = await Assert.ThrowsAsync<AiException>(() =>
            AlfaChatClient.LoginAsync(new HttpClient(handler), AlfaChatClient.DefaultBaseUrl, "me", "pw"));

        Assert.Contains("패스키", ex.Message);
    }

    [Fact]
    public async Task Login_refuses_a_remote_address()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"token":"t"}"""));

        await Assert.ThrowsAsync<AiException>(() =>
            AlfaChatClient.LoginAsync(new HttpClient(handler), "https://alfa.uziseignk.com", "me", "pw"));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void Alfa_waits_longer_than_the_server_thinks()
    {
        var s = new AiSettings();
        Assert.True(s.TimeoutFor(AiProvider.Alfa) > 90);
        Assert.Equal(s.TimeoutSeconds, s.TimeoutFor(AiProvider.Gemini));
        Assert.Equal("ALFA", AiSettings.DisplayName(AiProvider.Alfa));
    }
}
