using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using PortKiller.Core;
using Xunit;

namespace PortKiller.Core.Tests;

/// <summary>
/// ALFA 펫 연동 클라이언트 검사. 진짜 ALFA 는 부르지 않고 가짜 응답기로 "이런 답이 오면 무엇이 보이나"만 본다.
/// 로그인은 없다 — 연동 요청 → 승인 → 펫 키 받아 가기 → 펫 키로 묻기.
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

    private const string Key = "alfa_pet_test-key-not-real";

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (AlfaChatClient client, StubHandler handler) Alfa(
        Func<HttpRequestMessage, HttpResponseMessage> reply, string? key = Key, AiSettings? settings = null)
    {
        var handler = new StubHandler(reply);
        return (new AlfaChatClient(new HttpClient(handler), () => key, settings ?? new AiSettings()), handler);
    }

    private static readonly AlfaChatClient.LinkTicket Ticket =
        new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), "pickup-secret", "ABC123", DateTimeOffset.UtcNow.AddMinutes(10));

    [Fact]
    public async Task Asks_the_hub_pet_chat_with_the_pet_key()
    {
        var (client, handler) = Alfa(_ => Json(HttpStatusCode.OK, """{"reply":"alfa-backend 는 정상이에요.","source":"alorys-ai","status":"completed"}"""));

        var answer = await client.AskAsync("  alfa-backend 상태 봐 줘 ");

        Assert.Equal("alfa-backend 는 정상이에요.", answer);
        Assert.Equal("https://alfa.uziseignk.com/api/pet/chat", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal(Key, handler.LastRequest.Headers.Authorization.Parameter);
        Assert.Equal("alfa-backend 상태 봐 줘", JsonNode.Parse(handler.LastBody!)!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Approval_required_is_shown_and_never_confirmed_by_the_pet()
    {
        var (client, handler) = Alfa(_ => Json(HttpStatusCode.OK, """{"reply":"재시작할까요?","source":"alorys","status":"approval_required"}"""));

        var answer = await client.AskAsync("hermes 재시작해");

        Assert.StartsWith("재시작할까요?", answer);
        Assert.Contains("ALFA(웹·텔레그램)에서 승인", answer);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "PET_KEY_REQUIRED", "다시 연동")]
    [InlineData(HttpStatusCode.TooManyRequests, "CHAT_ALREADY_RUNNING", "앞 질문")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ALORYS_UNAVAILABLE", "응답하지 못해요")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ALORYS_ANSWER_INCOMPLETE", "끝까지")]
    public async Task Server_error_codes_become_plain_words(HttpStatusCode status, string code, string expected)
    {
        var (client, _) = Alfa(_ => Json(status, $$"""{"error":"{{code}}"}"""));

        var ex = await Assert.ThrowsAsync<AiException>(() => client.AskAsync("안녕"));

        Assert.Contains(expected, ex.Message);
        Assert.DoesNotContain(code, ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("a-user-session-token")]
    public async Task Without_a_pet_key_nothing_is_sent(string? key)
    {
        var (client, handler) = Alfa(_ => Json(HttpStatusCode.OK, "{}"), key: key);

        var ex = await Assert.ThrowsAsync<AiException>(() => client.AskAsync("안녕"));

        Assert.Contains("연동", ex.Message);
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
    [InlineData("http://alfa.uziseignk.com")]
    [InlineData("https://alfa.uziseignk.com.evil.example")]
    [InlineData("https://evil-uziseignk.com")]
    [InlineData("http://192.168.0.10:7701")]
    [InlineData("not a url")]
    public async Task The_key_is_never_sent_off_the_hub_or_this_pc(string url)
    {
        var (client, handler) = Alfa(_ => Json(HttpStatusCode.OK, "{}"), settings: new AiSettings { AlfaBaseUrl = url });

        await Assert.ThrowsAsync<AiException>(() => client.AskAsync("안녕"));
        await Assert.ThrowsAsync<AiException>(() => AlfaChatClient.PickupAsync(new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, "{}"))), url, Ticket));

        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("https://alfa.uziseignk.com", true)]
    [InlineData("https://uziseignk.com/", true)]
    [InlineData("http://127.0.0.1:7701", true)]
    [InlineData("http://localhost:7701/", true)]
    [InlineData("http://alfa.uziseignk.com", false)]
    [InlineData("https://uziseignk.com.example", false)]
    [InlineData("file:///c:/x", false)]
    public void Only_the_hub_over_https_or_this_pc_is_allowed(string url, bool expected)
        => Assert.Equal(expected, AlfaChatClient.IsAllowedBase(url));

    [Fact]
    public async Task Link_request_sends_device_and_pet_and_returns_the_code()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created,
            """{"linkId":"00000000-0000-0000-0000-0000000000a1","pickupSecret":"s3cret","confirmationCode":"A1B2C3","expiresAt":"2026-11-04T03:10:00Z"}"""));

        var ticket = await AlfaChatClient.RequestLinkAsync(new HttpClient(handler), AlfaChatClient.DefaultBaseUrl,
            "Windows Office", "레서판다", "red_panda", "windows");

        Assert.Equal("A1B2C3", ticket.ConfirmationCode);
        Assert.Equal("s3cret", ticket.PickupSecret);
        Assert.Equal("https://alfa.uziseignk.com/api/pet-links/request", handler.LastRequest!.RequestUri!.ToString());
        var body = JsonNode.Parse(handler.LastBody!)!;
        Assert.Equal("windows-office", body["deviceName"]!.GetValue<string>());
        Assert.Equal("windows", body["platform"]!.GetValue<string>());
        Assert.Null(handler.LastRequest.Headers.Authorization);
    }

    [Theory]
    [InlineData(HttpStatusCode.Accepted, """{"status":"pending"}""", AlfaChatClient.PickupState.Pending)]
    [InlineData(HttpStatusCode.Gone, """{"status":"closed"}""", AlfaChatClient.PickupState.Closed)]
    [InlineData(HttpStatusCode.Conflict, """{"error":"KEY_ALREADY_ISSUED"}""", AlfaChatClient.PickupState.AlreadyIssued)]
    [InlineData(HttpStatusCode.NotFound, """{"error":"PET_LINK_NOT_FOUND"}""", AlfaChatClient.PickupState.NotFound)]
    public async Task Pickup_states_before_and_after_approval(HttpStatusCode status, string body, AlfaChatClient.PickupState expected)
    {
        var handler = new StubHandler(_ => Json(status, body));

        var result = await AlfaChatClient.PickupAsync(new HttpClient(handler), AlfaChatClient.DefaultBaseUrl, Ticket);

        Assert.Equal(expected, result.State);
        Assert.Null(result.Key);
    }

    [Fact]
    public async Task Pickup_after_approval_returns_the_pet_key_once()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, $$"""{"status":"approved","key":"{{Key}}"}"""));

        var result = await AlfaChatClient.PickupAsync(new HttpClient(handler), AlfaChatClient.DefaultBaseUrl, Ticket);

        Assert.Equal(AlfaChatClient.PickupState.Issued, result.State);
        Assert.Equal(Key, result.Key);
        Assert.Equal("https://alfa.uziseignk.com/api/pet-links/00000000-0000-0000-0000-0000000000a1/pickup", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("pickup-secret", JsonNode.Parse(handler.LastBody!)!["pickupSecret"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_pickup_answer_without_a_pet_key_is_refused()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"status":"approved","key":"a-user-session-token"}"""));

        await Assert.ThrowsAsync<AiException>(() => AlfaChatClient.PickupAsync(new HttpClient(handler), AlfaChatClient.DefaultBaseUrl, Ticket));
    }

    [Theory]
    [InlineData("Windows Office", "windows-office")]
    [InlineData("  MAC-AIR ", "mac-air")]
    [InlineData("DESKTOP_ABC.local", "desktop_abc.local")]
    public void Device_names_take_the_shape_alfa_accepts(string raw, string expected)
        => Assert.Equal(expected, AlfaChatClient.NormalizeDeviceName(raw));

    [Fact]
    public void Alfa_waits_longer_than_the_server_thinks()
    {
        var s = new AiSettings();
        Assert.True(s.TimeoutFor(AiProvider.Alfa) > 90);
        Assert.Equal(s.TimeoutSeconds, s.TimeoutFor(AiProvider.Gemini));
        Assert.Equal("ALFA", AiSettings.DisplayName(AiProvider.Alfa));
    }
}
